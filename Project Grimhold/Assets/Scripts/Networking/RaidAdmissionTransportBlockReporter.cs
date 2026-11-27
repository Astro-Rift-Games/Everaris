using System;
using System.Threading.Tasks;
using Grimhold.Backend;
using UnityEngine;

/// <summary>
/// TASK-309: reports, once and analytics-only, that the local admission would be refused for
/// MaxTransportable. The report is fire-and-forget: it never alters, delays or aborts the launch,
/// and no failure of the send can escape.
/// </summary>
public sealed class RaidAdmissionTransportBlockReporter
{
    private readonly Func<Task<bool>> _send;

    /// <param name="send">Sends one report and returns whether it was accepted; null disables reporting.</param>
    public RaidAdmissionTransportBlockReporter(Func<Task<bool>> send)
    {
        _send = send;
    }

    /// <summary>Reporter over the authenticated backend session; disabled when there is none.</summary>
    public static RaidAdmissionTransportBlockReporter ForSession(ApplicationAuthContext auth)
    {
        if (!RaidLaunchBackend.RequiresConfirmation(auth)) return new RaidAdmissionTransportBlockReporter(null);

        BackendConfiguration config = auth.BackendConfig;
        string token = auth.Token;
        return new RaidAdmissionTransportBlockReporter(async () =>
            (await TelemetryClient.ReportTransportBlockAsync(config, token)).success);
    }

    /// <summary>Returns true when a report was dispatched for this admission.</summary>
    public bool ReportIfBlocked(in RaidAdmissionData admission, LootDefinitionCatalog catalog)
    {
        if (_send == null) return false;

        try
        {
            if (!RaidAdmissionTransportBlock.IsTransportLimitBlock(in admission, catalog)) return false;
        }
        catch (Exception ex)
        {
            // Analytics must never fail the launch that it observes.
            Debug.LogWarning(
                $"[RaidAdmissionTransportBlockReporter] Transport block detection failed ({ex.GetType().Name}).");
            return false;
        }

        _ = SendSafelyAsync();
        return true;
    }

    private async Task SendSafelyAsync()
    {
        try
        {
            if (!await _send())
                Debug.LogWarning("[RaidAdmissionTransportBlockReporter] Transport block report was not accepted.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[RaidAdmissionTransportBlockReporter] Transport block report failed ({ex.GetType().Name}).");
        }
    }
}
