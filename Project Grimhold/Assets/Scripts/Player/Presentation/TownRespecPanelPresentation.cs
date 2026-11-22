/// <summary>
/// Immutable projection of the Mentor respec panel (TASK-304): the confirmed attributes, the points a respec returns to
/// the pool, and the <see cref="TownRespecPresentation"/> (cost, availability, confirmation, result).
/// </summary>
public readonly struct TownRespecPanelPresentation
{
    /// <summary>Provisional display name of the respec NPC; Game Design may rename it.</summary>
    public const string Title = "Mentor";

    private const string Placeholder = "—";

    private readonly CharacterAttributeState _attributes;

    public bool HasAttributes { get; }

    /// <summary>Doc 04 §11: every assigned point returns to the pool, so this is the sum of the six attributes.</summary>
    public long PointsToReturn { get; }

    public TownRespecPresentation Respec { get; }

    public string PointsToReturnText =>
        $"Puntos que vuelven al pool: {(HasAttributes ? PointsToReturn.ToString() : Placeholder)}";

    private TownRespecPanelPresentation(
        in CharacterAttributeState attributes,
        bool hasAttributes,
        long pointsToReturn,
        in TownRespecPresentation respec)
    {
        _attributes = attributes;
        HasAttributes = hasAttributes;
        PointsToReturn = pointsToReturn;
        Respec = respec;
    }

    public static TownRespecPanelPresentation Create(in CharacterAttributeState attributes, in TownRespecPresentation respec) =>
        new(
            attributes,
            true,
            (long)attributes.Vitality + attributes.Resistance + attributes.Strength +
            attributes.Dexterity + attributes.Intelligence + attributes.Luck,
            respec);

    /// <summary>The build cannot be read: the panel shows placeholders instead of guessing.</summary>
    public static TownRespecPanelPresentation Unavailable(in TownRespecPresentation respec) =>
        new(default, false, 0L, respec);

    public string ValueText(CharacterAttribute attribute) =>
        HasAttributes && _attributes.TryGetValue(attribute, out int value) ? value.ToString() : Placeholder;
}
