using System;
using System.Collections.Generic;

/// <summary>
/// Declares which audio keys a config type may contain. <see cref="Required"/> keys must exist,
/// at least one <see cref="RequiredAnyOf"/> key must exist when that group is not empty, and
/// <see cref="Optional"/> keys are silent until content is added to the ScriptableObject.
/// Matching is case-sensitive against the canonical <see cref="AudioKeys"/> spelling, even though
/// runtime lookups are case-insensitive, so typos are caught by validation instead of failing silently.
/// </summary>
public sealed class AudioKeySpec
{
    private readonly HashSet<string> _known = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<string> Required { get; }
    public IReadOnlyList<string> RequiredAnyOf { get; }
    public IReadOnlyList<string> Optional { get; }

    public AudioKeySpec(string[] required, string[] requiredAnyOf, string[] optional)
    {
        Required = required ?? Array.Empty<string>();
        RequiredAnyOf = requiredAnyOf ?? Array.Empty<string>();
        Optional = optional ?? Array.Empty<string>();
        _known.UnionWith(Required);
        _known.UnionWith(RequiredAnyOf);
        _known.UnionWith(Optional);
    }

    public bool IsKnown(string key) => key != null && _known.Contains(key);

    /// <summary>Required keys (or the required any-of group) absent from the entry keys, by exact canonical spelling.</summary>
    public List<string> FindMissing(IEnumerable<string> entryKeys)
    {
        var present = new HashSet<string>(entryKeys, StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (string required in Required)
        {
            if (!present.Contains(required)) missing.Add(required);
        }

        if (RequiredAnyOf.Count > 0 && !AnyPresent(present, RequiredAnyOf))
        {
            missing.Add($"at least one of [{string.Join(", ", RequiredAnyOf)}]");
        }

        return missing;
    }

    /// <summary>Entry keys that are empty or not a canonical key of this spec (typos, wrong case, other config types' keys).</summary>
    public List<string> FindUnknown(IEnumerable<string> entryKeys)
    {
        var unknown = new List<string>();
        foreach (string key in entryKeys)
        {
            if (string.IsNullOrWhiteSpace(key)) unknown.Add("<empty key>");
            else if (!IsKnown(key)) unknown.Add(key);
        }

        return unknown;
    }

    /// <summary>Keys repeated under case-insensitive comparison (runtime lookups ignore case; only the first entry wins).</summary>
    public static List<string> FindDuplicates(IEnumerable<string> entryKeys)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();

        foreach (string key in entryKeys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            if (!seen.Add(key) && reported.Add(key)) duplicates.Add(key);
        }

        return duplicates;
    }

    private static bool AnyPresent(HashSet<string> keys, IReadOnlyList<string> candidates)
    {
        foreach (string candidate in candidates)
        {
            if (keys.Contains(candidate)) return true;
        }

        return false;
    }
}
