using System.Globalization;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>One of the three fixed CONTENTS slots. Absent slots stay in place.</summary>
public readonly record struct TierBadge(string Label, bool IsPresent);

/// <summary>
/// One row. "The row is the screen" - design section C.
///
/// Everything the row can say is decided here rather than in a converter or a trigger, which is
/// what makes the whole of screen 1's information design a table test with no WPF in it. The
/// TEMPLATE decides only how each of these reads: what colour, what rule style, which of the
/// two meta lines.
/// </summary>
public sealed class SnapshotRowViewModel : ObservableObject
{
    private static readonly string[] TierOrder = ["settings", "presets", "plugins"];

    private readonly SnapshotManifest manifest;
    private readonly DateTimeOffset now;

    private SnapshotHealth health;
    private HealthVerdict? verdict;
    private bool isSelected;
    private bool isEditing;
    private string draftName = string.Empty;
    private string? renameError;

    public SnapshotRowViewModel(
        Snapshot snapshot, int peakInputCount, DateTimeOffset now, string? query = null)
    {
        manifest = snapshot.Manifest;
        this.now = now;

        Id = snapshot.Id;
        Name = manifest.DisplayName;
        NameSegments = SnapshotSearch.Segments(manifest.DisplayName, query);

        // Suspect is known from the MANIFEST, without hashing anything - so an amber row is
        // right on the first frame rather than a quarter of a second later. Only DAMAGED has to
        // wait for the probe.
        health = manifest.IsSuspect ? SnapshotHealth.Suspect : SnapshotHealth.Whole;

        SizeBytes = manifest.Files.Values.Sum(f => f.SizeBytes);
        TakenAt = manifest.CreatedUtc.ToLocalTime();

        Slots = InputSlots.Build(manifest.InputNames, peakInputCount);
        Tiers = [.. TierOrder.Select(t => new TierBadge(
            t.ToUpper(CultureInfo.InvariantCulture),
            manifest.Tiers.Contains(t, StringComparer.OrdinalIgnoreCase)))];
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>
    /// Segments rather than a raw string, so the --wl-accent-soft highlight is a testable
    /// property of the view model instead of something hidden in a converter.
    /// </summary>
    public IReadOnlyList<NameSegment> NameSegments { get; }

    public long SizeBytes { get; }

    public DateTimeOffset TakenAt { get; }

    public IReadOnlyList<InputSlot> Slots { get; }

    public IReadOnlyList<TierBadge> Tiers { get; }

    public SnapshotHealth Health => health;

    public bool IsSuspect => health == SnapshotHealth.Suspect;

    public bool IsDamaged => health == SnapshotHealth.Damaged;

    public bool IsSelected
    {
        get => isSelected;
        set => Set(ref isSelected, value);
    }

    public string TakenTime => Readable.TimeOfDay(TakenAt);

    public string TakenDate => Readable.ShortDate(TakenAt);

    public string Why => manifest.Trigger switch
    {
        SnapshotTrigger.Manual => "MANUAL",
        SnapshotTrigger.Automatic => "AUTOMATIC",
        SnapshotTrigger.PreRestore => "PRE-RESTORE",
        _ => "UNKNOWN",
    };

    /// <summary>MANUAL is --wl-text; AUTOMATIC and PRE-RESTORE are --wl-muted (README).</summary>
    public bool WhyIsPrimary => manifest.Trigger == SnapshotTrigger.Manual;

    /// <summary>
    /// The sub-line under the name in the normal themes. Three states, three strings, each one
    /// README's or 02's own.
    /// </summary>
    public string MetaLine => health switch
    {
        SnapshotHealth.Damaged => "CHECKSUMS DON'T MATCH",
        SnapshotHealth.Suspect => $"{Readable.Bytes(SizeBytes)} · FAILED VALIDATION",
        _ => $"{Readable.Bytes(SizeBytes)} · {Readable.RelativeTime(TakenAt, now)}",
    };

    /// <summary>
    /// The same line in high contrast, where colour means nothing: a verdict WORD leads, and the
    /// rest follows. 11-high-contrast, and no new column - it renders in the NAME cell exactly
    /// where the meta line does.
    /// </summary>
    public string VerdictLine => health switch
    {
        SnapshotHealth.Damaged => "DAMAGED · CHECKSUMS DON'T MATCH",
        SnapshotHealth.Suspect => $"SUSPECT · {Readable.Bytes(SizeBytes)} · FAILED VALIDATION",
        _ => $"WHOLE · {Readable.Bytes(SizeBytes)}",
    };

    /// <summary>The pill beside the name, or null on a healthy row - which carries none.</summary>
    public string? HealthBadge => health switch
    {
        SnapshotHealth.Damaged => "DAMAGED",
        SnapshotHealth.Suspect => "SUSPECT",
        _ => null,
    };

    /// <summary>
    /// Rename and Restore are off for a damaged row; DELETE STAYS ON, because deleting it is
    /// the only useful thing left to do with it (02).
    ///
    /// A SUSPECT row keeps everything, Restore included - it is still restorable and may be the
    /// only copy that exists.
    /// </summary>
    public bool CanRename => !IsDamaged && !isEditing;

    public bool CanRestore => !IsDamaged;

    public bool CanDelete => true;

    /// <summary>
    /// In-place rename (README Interactions: "in place on the row's name; commit on Enter or blur,
    /// cancel on Escape"). While editing, the template swaps the segmented name for a TextBox bound
    /// to <see cref="DraftName"/> and hides the health pill; the meta line stays put. The commit
    /// itself is the list view model's job - it owns the store; this row only holds the draft and
    /// the inline cue, so both stay table-testable without a window.
    /// </summary>
    public bool IsEditing
    {
        get => isEditing;
        private set => Set(ref isEditing, value);
    }

    /// <summary>The name being typed. The TextBox binds this two-way while editing.</summary>
    public string DraftName
    {
        get => draftName;
        set => Set(ref draftName, value ?? string.Empty);
    }

    /// <summary>Inline cue under the name when a draft is rejected; null while valid or idle.</summary>
    public string? RenameError
    {
        get => renameError;
        private set => Set(ref renameError, value);
    }

    /// <summary>Enter edit mode: seed the draft from the current name and light the cue off.</summary>
    public void BeginEdit()
    {
        if (IsDamaged) return;

        isEditing = true;
        draftName = Name;
        renameError = null;

        Raise(nameof(IsEditing));
        Raise(nameof(DraftName));
        Raise(nameof(RenameError));
        Raise(nameof(CanRename));
    }

    /// <summary>
    /// Commit the draft. Validates with <see cref="RenameRules"/>; an invalid draft stays in edit
    /// mode and shows the cue (the user is not yanked out of the box they are typing in). A valid
    /// one commits to the store - on success the row leaves edit mode, on failure it shows the
    /// store's reason. Returns true when the commit landed so the caller can refresh.
    /// </summary>
    public bool TryCommitEdit(Func<string, string?> commitToStore)
    {
        var validation = RenameRules.Validate(draftName);
        if (!validation.IsValid)
        {
            renameError = validation.Reason;
            Raise(nameof(RenameError));
            return false;
        }

        var storeError = commitToStore(draftName);
        if (storeError is not null)
        {
            renameError = storeError;
            Raise(nameof(RenameError));
            return false;
        }

        isEditing = false;
        renameError = null;

        Raise(nameof(IsEditing));
        Raise(nameof(RenameError));
        Raise(nameof(CanRename));
        return true;
    }

    /// <summary>Leave edit mode without committing: the draft reverts to the stored name.</summary>
    public void CancelEdit()
    {
        if (!isEditing) return;

        isEditing = false;
        renameError = null;

        Raise(nameof(IsEditing));
        Raise(nameof(RenameError));
        Raise(nameof(CanRename));
    }

    /// <summary>
    /// README's selected-row readout. Presets and mixes are phase 6, so they are OMITTED rather
    /// than printed as zero - "3 PRESETS" when the tier does not exist yet would be a claim
    /// about contents nobody made.
    /// </summary>
    public string DetailLine
    {
        get
        {
            var effects = manifest.EffectCount == 1 ? "1 EFFECT" : $"{manifest.EffectCount} EFFECTS";
            var channels = manifest.EffectChannelCount == 1 ? "1 CHANNEL"
                : $"{manifest.EffectChannelCount} CHANNELS";

            return $"{effects} ON {channels}";
        }
    }

    /// <summary>
    /// What the backup is called on disk. README writes a ".wlbk" filename; this store keeps
    /// DIRECTORIES, and printing a filename that does not exist is worse than printing the name
    /// that does.
    /// </summary>
    public string DetailFileName => Id;

    /// <summary>02: "MANIFEST SAYS 470 KB · FILE IS 12 KB · CHECKED 23:09".</summary>
    public string? DamagedDetail
    {
        get
        {
            if (!IsDamaged || verdict is not { } v) return null;

            // Null, not zero. "The file is 0 B" is a measurement; "it can't be read" is what
            // actually happened, and they are different claims.
            var actual = v.ActualBytes is { } bytes
                ? $"FILE IS {Readable.Bytes(bytes)}"
                : "FILE CAN'T BE READ";

            return $"MANIFEST SAYS {Readable.Bytes(v.ManifestBytes)} · {actual} · "
                 + $"CHECKED {Readable.TimeOfDay(v.CheckedAt.ToLocalTime())}";
        }
    }

    /// <summary>02's sentence for the selected damaged row, verbatim.</summary>
    public string? DamagedSentence => IsDamaged
        ? "The files changed after this backup was written — a failed sync, a bad disk, or "
        + "something edited the folder. There is nothing left in here that can be trusted, so "
        + "it can't be restored. Delete it and take a fresh one."
        : null;

    /// <summary>
    /// What a screen reader hears instead of six unlabelled cells. Health leads, because a
    /// reader arriving at a row needs to know it cannot be restored before anything else.
    /// </summary>
    public string AutomationName
    {
        get
        {
            var state = health switch
            {
                SnapshotHealth.Damaged => "Damaged, cannot be restored. ",
                SnapshotHealth.Suspect => "Suspect, failed validation when it was taken. ",
                _ => string.Empty,
            };

            return $"{state}{Name}, {Why.ToLowerInvariant()}, taken "
                 + $"{Readable.ShortDate(TakenAt)} {Readable.TimeOfDay(TakenAt)}, "
                 + $"{Readable.Bytes(SizeBytes)}.";
        }
    }

    /// <summary>
    /// 7.4 is explicit that this is part of the work rather than a follow-up: the five-slot
    /// strip reads as five unlabelled cells without it, and which cells are filled is the entire
    /// meaning of the column.
    /// </summary>
    public string SlotsAutomationName =>
        manifest.InputNames.Count == 0
            ? $"No inputs, {InputSlots.SlotCount} slots empty."
            : $"{manifest.InputNames.Count} of {InputSlots.SlotCount} inputs: "
            + $"{string.Join(", ", manifest.InputNames)}.";

    /// <summary>
    /// The probe's answer, arriving after the row is already on screen. Raises every property it
    /// can move - without that the flip to DAMAGED happens in the object and nowhere else.
    /// </summary>
    public void ApplyVerdict(HealthVerdict verdict)
    {
        this.verdict = verdict;

        if (health == verdict.Health)
        {
            // Still raised: a damaged row re-probed after an F5 can have new sizes and a new
            // check time even though the verdict itself did not move.
            Raise(nameof(DamagedDetail));
            return;
        }

        health = verdict.Health;

        foreach (var property in (string[])
        [
            nameof(Health), nameof(IsSuspect), nameof(IsDamaged),
            nameof(MetaLine), nameof(VerdictLine), nameof(HealthBadge),
            nameof(CanRename), nameof(CanRestore), nameof(CanDelete),
            nameof(DamagedDetail), nameof(DamagedSentence), nameof(AutomationName),
        ])
        {
            Raise(property);
        }
    }
}
