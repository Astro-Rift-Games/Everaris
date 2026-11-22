using System;

/// <summary>
/// The Town build-mutation state shared by the attribute panel and the Mentor respec of one local player, so the two
/// cannot disagree about when the other must wait. The attribute presenter owns the instance; the respec presenter
/// reads the same Ready lock and busy flags and reports its own pending request back.
/// </summary>
public sealed class TownAttributeAssignmentGate
{
    private readonly Func<bool> _canMutateBuild;

    /// <param name="canMutateBuild">Ready-lock source (see <see cref="TownEquipmentMutationGate"/>); null means always allowed.</param>
    public TownAttributeAssignmentGate(Func<bool> canMutateBuild)
    {
        _canMutateBuild = canMutateBuild;
    }

    /// <summary>False while the local player is Ready, which locks every build change (Doc 04 §11).</summary>
    public bool CanMutateBuild => _canMutateBuild == null || _canMutateBuild();

    /// <summary>True while an attribute assignment awaits the backend.</summary>
    public bool IsCommitInFlight { get; set; }

    /// <summary>True while a respec request awaits the backend.</summary>
    public bool IsRespecPending { get; set; }

    /// <summary>An assignment waits for another assignment or a respec in flight.</summary>
    public bool IsAssignmentBlocked => IsCommitInFlight || IsRespecPending;

    /// <summary>A respec waits for an assignment in flight; its own pending state is guarded by the session.</summary>
    public bool IsRespecBlocked => IsCommitInFlight;
}
