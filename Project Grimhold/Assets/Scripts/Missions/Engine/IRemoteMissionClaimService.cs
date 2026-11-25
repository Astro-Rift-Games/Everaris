using System.Threading.Tasks;

/// <summary>
/// Remote-first boundary for authoritative mission reward claims (TASK-310).
/// </summary>
public interface IRemoteMissionClaimService
{
    Task<(StashOperationResult result, string feedbackMessage)> ClaimMissionAsync(MissionDefinition mission);
}
