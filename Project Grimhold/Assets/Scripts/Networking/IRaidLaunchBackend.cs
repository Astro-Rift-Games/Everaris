using System.Threading.Tasks;
using Grimhold.Backend;

/// <summary>The two backend operations required before a fresh Raid runner is created.</summary>
public interface IRaidLaunchBackend
{
    Task<(bool success, BackendError error)> SaveReservationAsync(
        ApplicationStashContext stash, PendingLoadoutReservation reservation);
    Task<(bool success, AuthoritativeRaidAdmissionTicketResponse data, BackendError error)> RequestTicketAsync(
        ApplicationAuthContext auth, RaidLaunchContext launch, PendingLoadoutReservation reservation);
}
