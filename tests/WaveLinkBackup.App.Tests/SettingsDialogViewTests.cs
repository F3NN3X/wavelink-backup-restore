using System.Windows;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Screen 3's view, forced through a real layout pass. The companion to
/// <see cref="RestoreDialogViewTests"/>, and added for the same reason: SettingsViewModelTests
/// covers the model thoroughly and nothing covered the Window, so the largest XAML file in the app
/// (five sections, three ItemsControls, six restyled controls) had no guard that it parses, resolves
/// its resources under App.xaml's real merge order, and lays out - the exact failure that had left
/// the restore dialog unopenable.
///
/// The click handlers reach App through <c>Application.Current as App</c>, which is null under the
/// test Application, so the null-conditional calls are no-ops here and constructing the window is
/// safe. RestoreFocus is likewise a no-op without a MainWindow owner.
/// </summary>
public sealed class SettingsDialogViewTests
{
    private const string Store = @"C:\Users\t\Backups";

    /// <summary>
    /// A model with every optional section populated, so nothing is skipped by a null. WhatGoesIn
    /// in particular is a settable property App fills in after Build, so a bare Build leaves the
    /// whole WHAT GOES IN A BACKUP section - four rows, the proportion bar and the two notes -
    /// rendering nothing, and every assertion about it would pass vacuously.
    /// </summary>
    private static SettingsViewModel Model()
    {
        var model = SettingsViewModel.Build(
            new BackupSettings(Store, AutoBackupEnabled: true),
            _ => true,
            new WhereSettingsLiveModel(@"C:\Users\t\AppData\Local\WaveLinkBackup\settings.json", "1.2 KB"),
            new WhichWaveLinkModel(
                "Wave Link 3.3.0.4108",
                @"C:\Users\t\AppData\Local\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState\Settings.json",
                new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero),
                Visible: true));

        // The design's own four tiers and their measured sizes (README Screen 3).
        model.WhatGoesIn = new WhatGoesInModel(
            new WhatGoesInRow("Your setup", "Channels, names, levels, routing.", 470_000, enabled: true, locked: true),
            new WhatGoesInRow("A list of your effects", "So a restore can tell you what's missing.", 4_000, enabled: true, locked: true),
            new WhatGoesInRow("Effect presets", "The settings you saved inside each effect.", 10_000_000, enabled: true, locked: false),
            new WhatGoesInRow("The effect plug-ins themselves", "Copies of the VST3 files.", 40_000_000, enabled: false, locked: false));

        return model;
    }

    private static void ShowAndAssert(AppTheme theme, Action<FrameworkElement> assert) => Wpf.Run(() =>
    {
        AppResources.Load(theme);

        // An explicit size: DialogOverlay only sizes this window when it has an owner, and a
        // layout assertion needs a deterministic one. Wide enough for the 680px card plus its
        // margins, tall enough that the body scrolls rather than the card being clipped.
        var dialog = new SettingsDialog(Model())
        {
            Width = 1000,
            Height = 800,
            Left = -3000,
            Top = -3000,
            ShowInTaskbar = false,
        };

        dialog.Show();
        dialog.UpdateLayout();

        // A second pass on purpose. The proportion bar's segment widths are computed from their
        // parent's ActualWidth, which only exists after the first ARRANGE - so the binding that
        // reads it cannot have produced a width during the first measure.
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
        // A style-target mismatch, an unresolvable StaticResource or a bad template all throw
        // during the layout pass, so "did not throw" is the assertion this file exists for.
        ShowAndAssert(theme, _ => { });
    }

    [Fact]
    public void Every_section_label_is_letter_spaced_at_the_column_header_role()
    {
        // README's type table puts section labels at mono 500, 10.5px, .18em UPPERCASE. Rendering
        // them as plain TextBlocks dropped the tracking - the one thing that separates the design's
        // micro-caps from a run of shouted text.
        var labels = new List<(string Text, double Tracking)>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e is TrackedText tracked && tracked.Text is { Length: > 0 } text &&
                text == text.ToUpperInvariant() && text.Contains(' '))
            {
                labels.Add((text, tracked.Tracking));
            }
        });

        Assert.Contains(labels, l => l.Text == "WHERE BACKUPS ARE KEPT");
        Assert.Contains(labels, l => l.Text == "WHAT GOES IN A BACKUP");
        Assert.All(labels, l => Assert.True(l.Tracking > 0, $"'{l.Text}' renders untracked."));
    }

    /// <summary>
    /// The bug a user reported seeing on screen: the two plain-language notes and the settings-file
    /// path rendered the literal strings "{Binding WhatGoesIn.NoteOneLead}",
    /// "{Binding WhatGoesIn.NoteOneRest}" and "{Binding WhereSettingsLive.FilePath}".
    ///
    /// The cause is a XAML rule with no compiler warning behind it: a markup extension is evaluated
    /// in ATTRIBUTE syntax only. Written as &lt;Run.Text&gt;{Binding X}&lt;/Run.Text&gt; - property-element
    /// syntax - the braces are just characters, so the binding never happens and the text prints
    /// itself. It builds, it parses, it renders; it is only wrong to look at.
    ///
    /// This reads the Runs the dialog actually produced, so it fails on the rendered text rather
    /// than on the source spelling.
    /// </summary>
    [Fact]
    public void No_rendered_text_is_an_unevaluated_binding_expression()
    {
        var literals = new List<string>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e is not System.Windows.Controls.TextBlock block) return;

            foreach (var inline in block.Inlines)
            {
                if (inline is System.Windows.Documents.Run { Text: { } text } &&
                    text.Contains("{Binding", StringComparison.Ordinal))
                {
                    literals.Add(text);
                }
            }

            if (block.Text.Contains("{Binding", StringComparison.Ordinal)) literals.Add(block.Text);
        });

        Assert.True(literals.Count == 0,
            "A binding expression is being shown to the user as text. Use Text=\"{Binding ...}\" " +
            "in attribute syntax, never <Run.Text>{Binding ...}</Run.Text>. Found: " +
            string.Join(" | ", literals.Distinct()));
    }

    /// <summary>The companion: the notes are not merely non-literal, they carry the real copy.</summary>
    [Fact]
    public void The_two_plain_language_notes_render_their_lead_clauses()
    {
        var text = new List<string>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e is System.Windows.Controls.TextBlock block) text.Add(block.Text);
        });

        Assert.Contains(text, t => t.StartsWith("Licences are never included.", StringComparison.Ordinal));
        Assert.Contains(text, t => t.StartsWith("A backup describes this computer.", StringComparison.Ordinal));
    }

    /// <summary>
    /// README Screen 3: "a 6px stacked proportion bar ... derived from the enabled tiers -
    /// recompute it, don't hard-code the percentages". WhatGoesInModel computes each segment's
    /// Fraction and SettingsViewModelTests pins the arithmetic - but a fraction nothing binds a
    /// WIDTH to draws a zero-width Border, so the bar renders as an empty sunken track and the
    /// computation is invisible.
    /// </summary>
    [Fact]
    public void The_proportion_bar_draws_a_segment_wide_enough_to_see()
    {
        var widths = new List<double>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e is System.Windows.Controls.Border { Name: "ProportionSegment" } segment)
            {
                widths.Add(segment.ActualWidth);
            }
        });

        Assert.NotEmpty(widths);
        Assert.All(widths, w => Assert.True(w > 0, "A proportion-bar segment rendered zero-width."));
    }

    [Fact]
    public void The_footer_says_changes_apply_as_you_make_them_and_offers_no_Save()
    {
        // "There is no Save button - every control commits immediately."
        var buttonLabels = new List<string>();

        ShowAndAssert(AppTheme.Dark, e =>
        {
            if (e is System.Windows.Controls.Button { Content: string label }) buttonLabels.Add(label);
        });

        Assert.Contains("Close", buttonLabels);
        Assert.DoesNotContain("Save", buttonLabels);
    }
}
