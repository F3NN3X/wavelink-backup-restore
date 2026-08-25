namespace WaveLinkBackup.Core.Snapshots;

/// <summary>A run of a snapshot's name, marked according to whether the query matched it.</summary>
public readonly record struct NameSegment(string Text, bool IsMatch);

/// <summary>
/// Filtering the list. PURE - it operates on an already-loaded list and touches no disk, so
/// typing in the search field costs nothing and needs no debounce.
///
/// Names ONLY. The search spec: "Search looks at names only. Say so
/// rather than implying full-text." The footer copy makes that promise to the user
/// ("SEARCH LOOKS AT NAMES ONLY"), so widening this later would make the copy a lie.
/// </summary>
public static class SnapshotSearch
{
    public static IReadOnlyList<Snapshot> Filter(IReadOnlyList<Snapshot> snapshots, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return snapshots;

        return [.. snapshots.Where(s =>
            s.Manifest.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase))];
    }

    /// <summary>
    /// Splits a name into matched and unmatched runs. The shell renders the matched runs on
    /// --wl-accent-soft; returning segments rather than a raw string is what keeps the
    /// highlighting testable instead of hiding it in a converter.
    ///
    /// Every occurrence is marked, not just the first.
    /// </summary>
    public static IReadOnlyList<NameSegment> Segments(string name, string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || name.Length == 0) return [new NameSegment(name, false)];

        var segments = new List<NameSegment>();
        var position = 0;

        while (position < name.Length)
        {
            var found = name.IndexOf(query, position, StringComparison.CurrentCultureIgnoreCase);
            if (found < 0) break;

            if (found > position) segments.Add(new NameSegment(name[position..found], false));

            // Slice from the NAME, not the query, so the row shows what the user actually
            // called the backup rather than what they happened to type.
            segments.Add(new NameSegment(name.Substring(found, query.Length), true));
            position = found + query.Length;
        }

        if (position < name.Length) segments.Add(new NameSegment(name[position..], false));

        return segments.Count == 0 ? [new NameSegment(name, false)] : segments;
    }
}
