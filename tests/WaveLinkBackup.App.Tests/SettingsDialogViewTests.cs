using System.Windows;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using Windows = WaveLinkBackup.App.Windows;
using Fakes = WaveLinkBackup.App.Tests.Fakes;
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

        // Drain the dispatcher so any binding still queued on this thread has resolved before the
        // assertion walks the tree. On a loaded CI runner the two synchronous UpdateLayout passes
        // can finish before a Run's OneWay binding has, leaving TextBlock.Text empty for a frame;
        // a local box resolves it inside UpdateLayout and never sees the gap.
        //
        // This used to be a single Invoke at Input priority, and the reasoning in its comment was
        // inverted - Invoke at priority P returns once everything HIGHER than P has run, so Input
        // (5) drains LESS than the Background (4) it replaced, not more. It also posts one marker
        // rather than running the loop, so anything the binding engine queues mid-drain lands
        // behind the marker and is still pending at assertion time. The flake duly came back on
        // 2026-08-25: one run green, one red, identical commit. Wpf.Drain pushes a frame at
        // SystemIdle instead, which is the bottom of the queue and keeps pumping.
        Wpf.Drain();

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

    // ------------------------------------------------- when to back up (screens/14-backup-timing)

    /// <summary>
    /// Shows the dialog over a caller-supplied model and hands back both the window's elements and
    /// the model, so a test can press a button and then read what it did.
    /// </summary>
    private static void ShowModel(SettingsViewModel model, Action<FrameworkElement> assert) => Wpf.Run(() =>
    {
        AppResources.Load(AppTheme.Dark);

        var dialog = new SettingsDialog(model)
        {
            Width = 1000,
            Height = 900,
            Left = -3000,
            Top = -3000,
            ShowInTaskbar = false,
        };

        dialog.Show();
        dialog.UpdateLayout();
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

    [Fact]
    public void Every_stepper_button_is_wired_to_something()
    {
        // The keep-count pair rendered and did NOTHING for the whole of phase 5 and 6: the buttons
        // were declared, the readout bound, and no handler existed. The view model's clamp was unit
        // tested and the wiring never was, so nothing caught it. This presses all six.
        var model = Model();
        var pressed = new List<string>();

        var keepBefore = model.AutoBackupKeepCount;
        var intervalBefore = model.AutoBackupIntervalMinutes;

        // Only the + halves, so an unwired handler cannot hide behind its opposite cancelling out.
        ShowModel(model, e =>
        {
            if (e is System.Windows.Controls.Button { Name.Length: > 0 } button
                && button.Name.StartsWith("Increment", StringComparison.Ordinal))
            {
                pressed.Add(button.Name);
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
        });

        // Every stepper's + was found, so none was silently skipped by a rename.
        Assert.Contains("IncrementKeepCountButton", pressed);
        Assert.Contains("IncrementIntervalButton", pressed);

        // And every one of them MOVED. This is the assertion the keep-count stepper would have
        // failed since phase 5.
        Assert.Equal(keepBefore + 1, model.AutoBackupKeepCount);
        Assert.True(model.AutoBackupIntervalMinutes > intervalBefore);
    }

    [Fact]
    public void The_daily_time_stepper_is_wired_too()
    {
        // Its row is hidden until the daily backup is on, so it needs its own pass - the test above
        // never reaches a collapsed element.
        var model = Model();
        model.DailyBackupEnabled = true;
        var before = model.DailyTimeText;
        var found = false;

        ShowModel(model, e =>
        {
            if (e.Name != "IncrementDailyTimeButton") return;

            found = true;
            e.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        });

        Assert.True(found);
        Assert.Equal("03:00", before);
        Assert.Equal("03:30", model.DailyTimeText);
    }

    [Fact]
    public void The_daily_time_row_appears_only_once_the_daily_backup_is_switched_on()
    {
        // The same rule the WHICH WAVE LINK section follows: a control with no effect is worse than
        // no control.
        var off = Model();
        var visibleWhenOff = false;
        ShowModel(off, e => visibleWhenOff |= e.Name == "DailyTimeRow" && e.IsVisible);

        var on = Model();
        on.DailyBackupEnabled = true;
        var visibleWhenOn = false;
        ShowModel(on, e => visibleWhenOn |= e.Name == "DailyTimeRow" && e.IsVisible);

        Assert.False(visibleWhenOff);
        Assert.True(visibleWhenOn);
    }

    // ------------- WHEN WINDOWS STARTS and error 9, drawn at last (technical-debt.md §4.21)

    /// <summary>
    /// The exact §4.20 lesson again: every autostart property was implemented and tested phases
    /// ago, and nothing rendered any of them. This walks the real visual tree, so a section that
    /// exists only in the model cannot pass.
    /// </summary>
    [Fact]
    public void The_when_windows_starts_section_is_drawn_and_its_toggles_are_bound()
    {
        var model = Model();
        var toggles = new List<string>();

        ShowModel(model, e =>
        {
            if (e is System.Windows.Controls.CheckBox { Name.Length: > 0 } box) toggles.Add(box.Name);
        });

        Assert.Contains("StartWithWindowsToggle", toggles);
        Assert.Contains("ClosingHidesToTrayToggle", toggles);
    }

    /// <summary>
    /// Pressing the real control has to move the real model — the half of the pair that a
    /// rendered-but-unbound toggle would still pass.
    /// </summary>
    [Fact]
    public void The_start_with_windows_toggle_writes_through_to_the_model()
    {
        var (model, _) = StartupModel();

        Assert.False(model.StartWithWindows);

        ShowModel(model, e =>
        {
            if (e.Name == "StartWithWindowsToggle" && e is System.Windows.Controls.CheckBox box)
            {
                box.IsChecked = true;
            }
        });

        Assert.True(model.StartWithWindows);
    }

    [Fact]
    public void Error_9_is_drawn_in_place_when_the_model_raises_it()
    {
        var model = Model();
        model.ShowNotABackupFolder(@"D:\Recordings\", 38);

        var visible = Visibility.Collapsed;

        ShowModel(model, e =>
        {
            if (e.Name == "NotABackupFolderBlock") visible = e.Visibility;
        });

        Assert.Equal(Visibility.Visible, visible);
    }

    [Fact]
    public void Error_9_stays_hidden_until_something_raises_it()
    {
        var found = false;
        var visible = Visibility.Visible;

        ShowModel(Model(), e =>
        {
            if (e.Name != "NotABackupFolderBlock") return;

            found = true;
            visible = e.Visibility;
        });

        Assert.True(found, "NotABackupFolderBlock is gone or renamed.");
        Assert.Equal(Visibility.Collapsed, visible);
    }

    /// <summary>A model carrying the real Run-key seam, over the fake registry.</summary>
    private static (SettingsViewModel Model, Fakes.FakeRegistryKeys Registry) StartupModel()
    {
        var registry = new Fakes.FakeRegistryKeys();
        var hides = true;

        var model = SettingsViewModel.Build(
            new BackupSettings(Store, AutoBackupEnabled: true),
            _ => true,
            new WhereSettingsLiveModel(@"C:\s\settings.json", "1 KB"),
            null,
            new StartupSeam(
                new Windows.RunKeyAutostart(registry, @"C:\p\WaveLinkBackup.exe"),
                () => hides,
                value => hides = value));

        return (model, registry);
    }

    // ------------- HOW IT LOOKS

    /// <summary>
    /// The §4.20 lesson once more: a section that exists only in the model passes every model test
    /// and paints nothing. This walks the real visual tree for the four segments.
    /// </summary>
    [Fact]
    public void The_how_it_looks_section_draws_all_four_theme_segments()
    {
        var segments = new List<string>();

        ShowModel(AppearanceModel().Model, e =>
        {
            if (e is System.Windows.Controls.RadioButton { Name.Length: > 0 } button)
            {
                segments.Add(button.Name);
            }
        });

        Assert.Contains("ThemeAutoSegment", segments);
        Assert.Contains("ThemeDarkSegment", segments);
        Assert.Contains("ThemeLightSegment", segments);
        Assert.Contains("ThemeHighContrastSegment", segments);
    }

    /// <summary>
    /// Pressing the real control has to move the real model - the half a rendered-but-unbound
    /// segment would still pass.
    /// </summary>
    [Fact]
    public void Pressing_a_theme_segment_writes_through_to_the_model()
    {
        var (model, written) = AppearanceModel();

        Assert.True(model.ThemeIsAuto);

        ShowModel(model, e =>
        {
            if (e.Name == "ThemeLightSegment" && e is System.Windows.Controls.RadioButton button)
            {
                button.IsChecked = true;
            }
        });

        Assert.Equal(ThemePreference.Light, model.Theme);
        Assert.Equal([ThemePreference.Light], written);
    }

    /// <summary>
    /// The stored preference has to be ON the segment when the dialog opens, not merely readable
    /// from the model - an IsChecked binding that only travels one way leaves every segment
    /// looking unpicked.
    /// </summary>
    [Fact]
    public void The_stored_preference_opens_already_selected()
    {
        var (model, _) = AppearanceModel(ThemePreference.HighContrast);
        var checkedNames = new List<string>();

        ShowModel(model, e =>
        {
            if (e is System.Windows.Controls.RadioButton { IsChecked: true } button)
            {
                checkedNames.Add(button.Name);
            }
        });

        Assert.Equal(["ThemeHighContrastSegment"], checkedNames);
    }

    /// <summary>A model carrying a real appearance seam over a plain variable.</summary>
    private static (SettingsViewModel Model, List<ThemePreference> Written) AppearanceModel(
        ThemePreference stored = ThemePreference.Auto)
    {
        var written = new List<ThemePreference>();
        var current = stored;

        var model = SettingsViewModel.Build(
            new BackupSettings(Store, AutoBackupEnabled: true),
            _ => true,
            new WhereSettingsLiveModel(@"C:\s\settings.json", "1 KB"),
            null,
            null,
            new AppearanceSeam(
                () => current,
                value => { current = value; written.Add(value); },
                () => current == ThemePreference.HighContrast));

        return (model, written);
    }
}
