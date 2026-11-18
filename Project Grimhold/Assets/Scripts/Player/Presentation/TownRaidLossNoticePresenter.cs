using System;
using UnityEngine;

/// <summary>
/// Scene-resident Town presenter of the loss notice a technical Raid failure leaves behind.
/// It reads the one-shot notice from the application-owned coordinator, so it works after the
/// Raid runner is gone, and shows it until the player continues. While the notice is open it
/// holds one gameplay-input suppression, so movement, attacks and the camera cursor offset stay
/// still behind the modal.
/// </summary>
[DisallowMultipleComponent]
public sealed class TownRaidLossNoticePresenter : MonoBehaviour
{
    [SerializeField] private TownRaidLossNoticeView _view;

    private PlayerInputReader _inputReader;
    private IDisposable _inputSuppression;

    /// <summary>
    /// Explicit reader binding for initialization and tests. Without it the scene's local reader is
    /// resolved when the notice is shown.
    /// </summary>
    public void BindInputReader(PlayerInputReader reader)
    {
        if (_inputReader == reader)
        {
            return;
        }

        ReleaseInputSuppression();
        _inputReader = reader;
        if (_view != null && _view.IsOpen)
        {
            AcquireInputSuppression();
        }
    }

    private void OnEnable()
    {
        if (_view == null)
        {
            return;
        }

        _view.ContinueRequested += OnContinueRequested;
        if (_view.IsOpen)
        {
            AcquireInputSuppression();
        }
    }

    private void OnDisable()
    {
        if (_view != null)
        {
            _view.ContinueRequested -= OnContinueRequested;
        }

        ReleaseInputSuppression();
    }

    private void Update()
    {
        SessionConnectionCoordinator coordinator = SessionConnectionCoordinator.Instance;
        if (_view == null || _view.IsOpen || coordinator == null ||
            coordinator.State != SessionConnectionState.Town ||
            !coordinator.TryConsumePendingLossNotice(out RaidLossNotice notice))
        {
            return;
        }

        Show(notice);
    }

    /// <summary>Presents the notice and holds gameplay-input suppression until the player continues.</summary>
    public void Show(RaidLossNotice notice)
    {
        if (_view == null)
        {
            return;
        }

        ApplicationStashContext stash = FindAnyObjectByType<ApplicationStashContext>();
        LootDefinitionCatalog catalog = stash != null && stash.Store != null ? stash.Store.LootCatalog : null;
        _view.Present(TownRaidLossNoticePresentation.Build(notice, lootId => ResolveName(catalog, lootId)));
        if (_view.IsOpen)
        {
            AcquireInputSuppression();
        }

        Debug.Log(
            $"[FIX-53] Loss notice shown. Reason={notice.Reason}, Items={notice.Lost.Entries.Count}.",
            this);
    }

    private void OnContinueRequested()
    {
        _view.Hide();
        ReleaseInputSuppression();
        Debug.Log("[FIX-53] Loss notice dismissed.", this);
    }

    private void AcquireInputSuppression()
    {
        if (_inputSuppression != null)
        {
            return;
        }

        PlayerInputReader reader = _inputReader != null ? _inputReader : FindAnyObjectByType<PlayerInputReader>();
        if (reader == null)
        {
            Debug.LogWarning(
                $"{nameof(TownRaidLossNoticePresenter)} found no {nameof(PlayerInputReader)}; gameplay input stays active behind the notice.",
                this);
            return;
        }

        _inputSuppression = reader.AcquireGameplayInputSuppression();
    }

    private void ReleaseInputSuppression()
    {
        _inputSuppression?.Dispose();
        _inputSuppression = null;
    }

    private static string ResolveName(LootDefinitionCatalog catalog, LootId lootId) =>
        catalog != null && catalog.TryGet(lootId.Value, out LootDefinition definition) && definition != null
            ? definition.DisplayName
            : null;
}
