using System.Windows;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.Core.Restore;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Screen 2's view, forced through a real layout pass - the same idiom as
/// <see cref="ErrorDialogViewTests"/> and DeleteDialogViewTests, and the guard those two had that
/// this dialog did not.
///
/// The gap was not theoretical. RestoreDialog.xaml applied WlColumnHeaderText - a Style with
/// TargetType="TextBlock" - to the NOW / AFTER RESTORE headers, which are TrackedText elements
/// (a FrameworkElement, deliberately not a TextBlock). WPF throws on a TargetType mismatch when the
/// style is applied, so the app's only irreversible action could not be confirmed at all: opening
/// the dialog threw instead of rendering. Nothing caught it because no test ever instantiated this
/// window.
///
/// Showing the window IS the assertion. A style mismatch, an unresolvable StaticResource or a bad
/// binding target all throw during that pass; the per-element checks below are the second layer.
/// </summary>
public sealed class RestoreDialogViewTests
{
    private static readonly DateTimeOffset Taken = new(2026, 8, 11, 21, 36, 0, TimeSpan.Zero);

    /// <summary>The design's own sample plan (README Screen 2), including a changed row.</summary>
    private static RestorePlan Plan(string? versionWarning = null) => new(
        SnapshotName: "Before 3.3 beta",
        SnapshotTakenUtc: Taken,
        Rows:
        [
            new PlanRow("Inputs", "5 — all named", "5 — all named", Changes: false),
            new PlanRow("Effects", "12 on 3 channels", "17 on 4 channels", Changes: true),
        ],
        LosesInputs: false,
        InputNamesLost: [],
        SnapshotIsSuspect: false,
        VersionWarning: versionWarning);

    private static void ShowAndAssert(
        RestoreDialogModel model, AppTheme theme, Action<FrameworkElement> assert) => Wpf.Run(() =>
    {
        AppResources.Load(theme);

        var dialog = new RestoreDialog(model)
        {
            Width = 720,
            Height = 640,
            Left = -3000,
            Top = -3000,
            ShowInTaskbar = false,
        };

        dialog.Show();
        dialog.UpdateLayout();

        try
        {
            foreach (var element in AppResources.Descendants(dialog)) assert(element);
        }
        finally
        {
            dialog.Close();
        }

        return true;
    });

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.HighContrast)]
    public void The_dialog_renders_in_every_theme(AppTheme theme)
    {
        // Not showing at all was the real failure mode here, so "did not throw" is the assertion.
        ShowAndAssert(RestoreDialogModel.Build(Plan(), Taken), theme, _ => { });
    }

    [Fact]
    public void Both_footer_buttons_are_present_and_Cancel_holds_focus()
    {
        // 10-decisions section 6: focus starts on Cancel for an irreversible action.
        var seen = new List<string>();

        ShowAndAssert(RestoreDialogModel.Build(Plan(), Taken), AppTheme.Dark, e =>
        {
            if (e.Name is "CancelButton" or "RestoreButton") seen.Add(e.Name);
            if (e.Name == "CancelButton") Assert.True(e.IsKeyboardFocusWithin);
        });

        Assert.Equal(["CancelButton", "RestoreButton"], seen);
    }
}
