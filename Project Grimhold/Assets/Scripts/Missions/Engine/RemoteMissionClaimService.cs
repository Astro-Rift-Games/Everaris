using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grimhold.Backend;
using UnityEngine;

/// <summary>
/// Remote-first mission claim persistence boundary (TASK-310):
/// 1. Pre-validates local claim eligibility (state, delivery items, Stash capacity).
/// 2. Awaits backend acknowledgement for authoritative Gold credit and mission_reward telemetry.
/// 3. Commits the local mission claim using the exact authoritative currency and revision returned
///    by the backend. Reuses the same timestamped transactionId across retries of the same claimKey.
/// </summary>
public sealed class RemoteMissionClaimService : MonoBehaviour, IRemoteMissionClaimService
{
    public delegate Task<(bool success, ClaimMissionResult data, BackendError error)> BackendCall(
        string transactionId,
        string missionId,
        string claimKey,
        int expectedRevision);

    private readonly Dictionary<string, string> _pendingTransactionIds = new(StringComparer.Ordinal);
    private LocalProfileStore _store;
    private BackendCall _backend;
    private Func<Task<bool>> _reconcile;

    public void Initialize(LocalProfileStore store, ProfileReconciliationService reconciliationService)
    {
        if (reconciliationService == null) throw new ArgumentNullException(nameof(reconciliationService));
        BackendConfiguration config = BackendConfigurationProvider.Resolve();
        if (config == null)
        {
            Debug.LogError("[RemoteMissionClaimService] BackendConfiguration is missing; authoritative mission claims are unavailable.");
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _backend = null;
            return;
        }

        Initialize(
            store,
            (transactionId, missionId, claimKey, revision) => MissionClient.ClaimMissionAsync(
                config,
                ApplicationAuthContext.Instance?.Token,
                transactionId,
                missionId,
                claimKey,
                revision),
            reconciliationService.ReconcileAsync);
    }

    /// <summary>Explicit infrastructure injection, also used by deterministic EditMode tests.</summary>
    public void Initialize(LocalProfileStore store, BackendCall backend, Func<Task<bool>> reconcile)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _backend = backend;
        _reconcile = reconcile;
    }

    public string BuildClaimKey(MissionDefinition mission)
    {
        if (mission == null || !mission.MissionId.IsValid) return string.Empty;
        if (mission.Type == MissionType.Unica)
        {
            return mission.Id;
        }

        string windowId = _store != null
            ? _store.GetCurrentDailyRotationWindow().WindowId
            : DailyContractRotationEngine.ComputeWindow(DateTimeOffset.UtcNow).WindowId;
        return $"{mission.Id}:{windowId}";
    }

    public async Task<(StashOperationResult result, string feedbackMessage)> ClaimMissionAsync(MissionDefinition mission)
    {
        if (_store == null || !_store.IsAvailable || mission == null || !mission.MissionId.IsValid)
        {
            return (StashOperationResult.InvalidInventory, "No se puede reclamar el contrato en este momento.");
        }

        if (!_store.CanClaimMission(mission, out string reason))
        {
            var failureResult = reason != null && reason.Contains("Espacio insuficiente en el Alijo")
                ? StashOperationResult.InventoryFull
                : StashOperationResult.InvalidInventory;
            return (failureResult, reason);
        }

        if (_backend == null)
        {
            return (StashOperationResult.PersistenceFailed, "El servidor de misiones no está disponible.");
        }

        string claimKey = BuildClaimKey(mission);
        if (!_pendingTransactionIds.TryGetValue(claimKey, out string transactionId))
        {
            transactionId = new ShopTransactionId(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Guid.NewGuid()).ToString();
            _pendingTransactionIds[claimKey] = transactionId;
        }

        (bool success, ClaimMissionResult data, BackendError error) response;
        try
        {
            response = await _backend(transactionId, mission.Id, claimKey, _store.RemoteRevision);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RemoteMissionClaimService] Backend outcome unknown for '{mission.Id}': {exception.Message}");
            _ = ReconcileSafelyAsync();
            return (StashOperationResult.PersistenceFailed, "No se pudo confirmar el reclamo con el servidor. Intentá nuevamente.");
        }

        if (response.success)
        {
            _pendingTransactionIds.Remove(claimKey);
            try
            {
                var commitResult = _store.TryClaimMissionWithAuthority(
                    mission,
                    authoritativeCurrency: response.data.currency,
                    authoritativeRevision: response.data.revision);

                if (commitResult != StashOperationResult.Success)
                {
                    Debug.LogError($"[RemoteMissionClaimService] Backend persisted mission claim '{mission.Id}' but local commit failed ({commitResult}); reconciling.");
                    _ = ReconcileSafelyAsync();
                    return (commitResult, $"Error al actualizar el perfil local tras el reclamo: {commitResult}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[RemoteMissionClaimService] Backend persisted mission claim '{mission.Id}' but local persistence threw: {exception.Message}");
                _ = ReconcileSafelyAsync();
                return (StashOperationResult.PersistenceFailed, "Error local tras confirmar el reclamo en el servidor.");
            }

            return (StashOperationResult.Success, $"¡Recompensa del contrato '{mission.Title}' reclamada con éxito!");
        }

        if (response.error.error == BackendErrorUtility.Unauthorized)
        {
            ApplicationAuthContext.Instance?.HandleUnauthorizedSession();
            return (StashOperationResult.PersistenceFailed, "Tu sesión expiró. Iniciá sesión nuevamente.");
        }

        // Terminal rejections clear the pending transactionId so a later valid attempt gets a fresh id.
        if (response.error.error is "MISSION_ALREADY_CLAIMED" or "MISSION_NOT_FOUND" or "VALIDATION_FAILED" or "TRANSACTION_ID_REUSED")
        {
            _pendingTransactionIds.Remove(claimKey);
        }

        _ = ReconcileSafelyAsync();
        return response.error.error switch
        {
            "RESERVATION_PENDING" => (StashOperationResult.ReservationPending, "No podés reclamar contratos con una incursión pendiente."),
            "MISSION_ALREADY_CLAIMED" => (StashOperationResult.AlreadyApplied, "Esta recompensa de contrato ya fue reclamada."),
            "REVISION_CONFLICT" => (StashOperationResult.InvalidInventory, "El perfil se sincronizó con el servidor. Intentá reclamar nuevamente."),
            "MISSION_NOT_FOUND" or "VALIDATION_FAILED" or "TRANSACTION_RETENTION_EXCEEDED" or "TRANSACTION_ID_REUSED" =>
                (StashOperationResult.InvalidInventory, $"Reclamo rechazado por el servidor ({response.error.error})."),
            _ => (StashOperationResult.PersistenceFailed, $"Error del servidor al reclamar recompensa ({response.error.error}).")
        };
    }

    private async Task ReconcileSafelyAsync()
    {
        try
        {
            if (_reconcile != null && !await _reconcile())
            {
                Debug.LogWarning("[RemoteMissionClaimService] Reconciliation failed.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RemoteMissionClaimService] Reconciliation failed: {exception.Message}");
        }
    }
}
