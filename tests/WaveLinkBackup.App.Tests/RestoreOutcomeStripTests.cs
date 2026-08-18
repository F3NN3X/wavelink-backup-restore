using System.ComponentModel;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The strip's state machine, in isolation from the window. 03-restore-outcomes.md is
/// authoritative for the four outcomes and their dismiss rules; these tests assert exactly that
/// behaviour without standing up a Window, because the XAML binds to the booleans this class
/// exposes (IsVisible, HasLeftEdge, TurnsStatusAmber, Dismissible, HasAction) and those are what
/// must be right.
///
/// A RestoreOutcome is built by hand rather than round-tripped through an orchestrator: the strip
/// only reads PreRestoreSnapshot's presence, Relaunched, and Verdict - it never touches the store.
/// </summary>
public sealed class RestoreOutcomeStripTests
{
    // A minimal but real outcome: the strip ignores everything about the snapshot except that one
    // exists, so a placeholder manifest is enough.
    private static RestoreOutcome Outcome(RestoreVerdict? verdict) => new(
        PreRestoreSnapshot: new Snapshot(
            "pre-restore",
            @"C:\Users\test\AppData\Local\WaveLinkBackup\pre-restore",
            new SnapshotManifest(
                SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
                DisplayName: "Before restore",
                Notes: string.Empty,
                CreatedUtc: DateTimeOffset.UnixEpoch,
                Trigger: SnapshotTrigger.PreRestore,
                SettingsSha256: new string('0', 64),
                WaveLinkVersion: null,
                InputCount: 3,
                InputNames: ["Wave Mic 1"],
                EffectCount: 0,
                EffectChannelCount: 0,
                HasDuplicateKeys: false,
                Tiers: [],
                Files: new Dictionary<string, SnapshotFile>())),
        Relaunched: true,
        Verdict: verdict);

    private static RestoreVerdict ConfirmedVerdict() => new(
        ParseFailed: false,
        CreatedNewBackup: false,
        AppliedNames: ["Wave Mic 1", "Voice"],
        Version: "3.3.0.4108",
        Channel: "Beta");

    // -------------------------------------------------------------- the four outcomes

    [Fact]
    public void A_confirmed_success_is_quiet_and_auto_dismisses()
    {
        var strip = new RestoreOutcomeStrip();

        strip.Show(Outcome(ConfirmedVerdict()));

        Assert.Equal(RestoreStripKind.SucceededConfirmed, strip.Kind);
        Assert.True(strip.IsVisible);
        Assert.False(strip.HasLeftEdge);
        Assert.False(strip.TurnsStatusAmber);
        Assert.True(strip.AutoDismisses);
        Assert.True(strip.Dismissible);
        Assert.False(strip.HasAction);
    }

    [Fact]
    public void A_confirmed_success_names_what_was_applied_and_the_version()
    {
        var strip = new RestoreOutcomeStrip();

        strip.Show(Outcome(ConfirmedVerdict()));

        Assert.Equal("Restore confirmed", strip.Title);
        Assert.Contains("2 channel names", strip.Detail, StringComparison.Ordinal);
        Assert.Contains("Wave Link 3.3.0.4108", strip.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_applied_name_is_not_pluralised()
    {
        var strip = new RestoreOutcomeStrip();

        strip.Show(Outcome(new RestoreVerdict(false, false, ["Wave Mic 1"], "3.3.0.4108", null)));

        Assert.Contains("1 channel name ", strip.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("channel names", strip.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parse_failure_is_a_reject_not_dismissible_and_turns_the_status_amber()
    {
        var strip = new RestoreOutcomeStrip();

        strip.Show(Outcome(new RestoreVerdict(
            ParseFailed: true, CreatedNewBackup: true, AppliedNames: [], Version: "3.3.0.4108", Channel: null)));

        Assert.Equal(RestoreStripKind.Rejected, strip.Kind);
        Assert.True(strip.IsVisible);
        Assert.True(strip.HasLeftEdge);
        Assert.True(strip.TurnsStatusAmber);
        Assert.False(strip.AutoDismisses);
        Assert.False(strip.Dismissible);
        Assert.False(strip.HasAction);
    }

    [Fact]
    public void A_reject_says_wave_link_rejected_the_file()
    {
        var strip = new RestoreOutcomeStrip();

        strip.Show(Outcome(new RestoreVerdict(true, true, [], "3.3.0.4108", null)));

        Assert.Equal("Wave Link rejected the settings file", strip.Title);
    }

    [Fact]
    public void A_success_without_applied_names_is_unconfirmed_neutral_and_offers_to_check_again()
    {
        var strip = new RestoreOutcomeStrip();

        // No parse failure, but the log recorded nothing Wave Link applied: the write went
        // through, the confirmation did not. This is NOT a reject - no amber, no left edge.
        strip.Show(Outcome(new RestoreVerdict(false, false, [], "3.3.0.4108", null)));

        Assert.Equal(RestoreStripKind.SucceededUnconfirmed, strip.Kind);
        Assert.False(strip.HasLeftEdge);
        Assert.False(strip.TurnsStatusAmber);
        Assert.False(strip.AutoDismisses);
        Assert.True(strip.Dismissible);
        Assert.True(strip.HasAction);
        Assert.Equal("Check again", strip.ActionLabel);
    }

    [Fact]
    public void An_unreadable_log_is_unconfirmed_never_a_reject()
    {
        var strip = new RestoreOutcomeStrip();

        // Verdict null: the log could not be read at all. 03-restore-outcomes.md treats this as
        // unconfirmed (neutral), never as a reject - a missing log is not evidence of a parse
        // failure.
        strip.Show(Outcome(null));

        Assert.Equal(RestoreStripKind.SucceededUnconfirmed, strip.Kind);
        Assert.False(strip.HasLeftEdge);
        Assert.False(strip.TurnsStatusAmber);
        Assert.True(strip.Dismissible);
        Assert.True(strip.HasAction);
        Assert.Equal("Check again", strip.ActionLabel);
    }

    [Fact]
    public void A_failed_restore_is_danger_and_dismissible()
    {
        var strip = new RestoreOutcomeStrip();

        strip.ShowFailure("Wave Link would not close after 30 seconds.");

        Assert.Equal(RestoreStripKind.Failed, strip.Kind);
        Assert.True(strip.IsVisible);
        Assert.False(strip.HasLeftEdge);
        Assert.False(strip.TurnsStatusAmber);
        Assert.False(strip.AutoDismisses);
        Assert.True(strip.Dismissible);
        Assert.False(strip.HasAction);
        Assert.Equal("Restore failed", strip.Title);
        Assert.Equal("Wave Link would not close after 30 seconds.", strip.Detail);
    }

    // -------------------------------------------------------------- dismiss rules

    [Fact]
    public void A_dismissible_strip_hides_itself()
    {
        var strip = new RestoreOutcomeStrip();
        strip.Show(Outcome(ConfirmedVerdict()));

        strip.Dismiss();

        Assert.Equal(RestoreStripKind.None, strip.Kind);
        Assert.False(strip.IsVisible);
        Assert.False(strip.HasAction);
        Assert.False(strip.Dismissible);
    }

    [Fact]
    public void A_reject_ignores_dismiss_until_acted_on()
    {
        var strip = new RestoreOutcomeStrip();
        strip.Show(Outcome(new RestoreVerdict(true, true, [], "3.3.0.4108", null)));

        strip.Dismiss();

        // The whole point of the rule: dismissing would hide the one piece of evidence that says
        // WHY Wave Link rejected the file. It stays up.
        Assert.Equal(RestoreStripKind.Rejected, strip.Kind);
        Assert.True(strip.IsVisible);
    }

    [Fact]
    public void AcknowledgeReject_is_the_only_way_a_reject_goes_away()
    {
        var strip = new RestoreOutcomeStrip();
        strip.Show(Outcome(new RestoreVerdict(true, true, [], "3.3.0.4108", null)));

        strip.AcknowledgeReject();

        Assert.Equal(RestoreStripKind.None, strip.Kind);
        Assert.False(strip.IsVisible);
    }

    [Fact]
    public void AcknowledgeReject_does_nothing_when_there_is_no_reject()
    {
        var strip = new RestoreOutcomeStrip();
        strip.Show(Outcome(ConfirmedVerdict()));

        strip.AcknowledgeReject();

        Assert.Equal(RestoreStripKind.SucceededConfirmed, strip.Kind);
        Assert.True(strip.IsVisible);
    }

    [Fact]
    public void Dismiss_does_nothing_when_the_strip_is_already_hidden()
    {
        var strip = new RestoreOutcomeStrip();

        strip.Dismiss();

        Assert.Equal(RestoreStripKind.None, strip.Kind);
        Assert.False(strip.IsVisible);
    }

    // -------------------------------------------------------------- the action seam

    [Fact]
    public void The_action_button_invokes_the_seam_when_set()
    {
        var strip = new RestoreOutcomeStrip();
        strip.Show(Outcome(new RestoreVerdict(false, false, [], "3.3.0.4108", null)));

        var invoked = 0;
        strip.OnAction = () => invoked++;

        strip.OnAction?.Invoke();

        Assert.Equal(1, invoked);
    }

    [Fact]
    public void The_action_seam_is_inert_when_not_set()
    {
        var strip = new RestoreOutcomeStrip();
        strip.Show(Outcome(new RestoreVerdict(false, false, [], "3.3.0.4108", null)));

        // No throw: the window wires OnAction only when it has something to do; a null seam must
        // be a no-op rather than an NRE on the unconfirmed strip's "Check again".
        strip.OnAction?.Invoke();

        Assert.True(strip.HasAction);
    }

    // -------------------------------------------------------------- change notification

    [Fact]
    public void Showing_a_strip_raises_kind_and_the_derived_booleans()
    {
        var strip = new RestoreOutcomeStrip();
        var raised = new List<string?>();

        strip.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        strip.Show(Outcome(new RestoreVerdict(true, true, [], "3.3.0.4108", null)));

        Assert.Contains(nameof(RestoreOutcomeStrip.Kind), raised);
        Assert.Contains(nameof(RestoreOutcomeStrip.IsVisible), raised);
        Assert.Contains(nameof(RestoreOutcomeStrip.HasLeftEdge), raised);
        Assert.Contains(nameof(RestoreOutcomeStrip.TurnsStatusAmber), raised);
        Assert.Contains(nameof(RestoreOutcomeStrip.Dismissible), raised);
    }

    [Fact]
    public void The_auto_dismiss_interval_is_six_seconds()
    {
        // 03-restore-outcomes.md: the quiet "succeeded + confirmed" strip clears itself after six
        // seconds. The window's DispatcherTimer reads this constant, so it is asserted here where
        // the design decision lives rather than in a test that stands up a timer.
        Assert.Equal(TimeSpan.FromSeconds(6), RestoreOutcomeStrip.AutoDismissAfter);
    }
}
