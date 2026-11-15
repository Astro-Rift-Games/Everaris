using System.Threading.Tasks;
using Grimhold.Backend;

public sealed class RaidLaunchBackend : IRaidLaunchBackend
{
    // Keep the offline/Editor exception identical at preparation and at runner startup.
    public static bool RequiresConfirmation(ApplicationAuthContext auth) =>
        auth != null && auth.IsAuthenticated && auth.BackendConfig != null && !string.IsNullOrEmpty(auth.Token);

    public Task<(bool success, BackendError error)> SaveReservationAsync(
        ApplicationStashContext stash, PendingLoadoutReservation reservation) =>
        stash.TryGetComponent(out RemoteInventoryService remote)
            ? remote.SavePendingReservationAsync(reservation)
            : Task.FromResult((false, new BackendError { error = "RESERVATION_SERVICE_UNAVAILABLE" }));

    public Task<(bool success, AuthoritativeRaidAdmissionTicketResponse data, BackendError error)> RequestTicketAsync(
        ApplicationAuthContext auth, RaidLaunchContext launch, PendingLoadoutReservation reservation) =>
        CharacterClient.GetAuthoritativeRaidAdmissionTicketAsync(
            auth.BackendConfig, auth.Token, launch.RaidCode.Value, reservation.ReservationId);
}
