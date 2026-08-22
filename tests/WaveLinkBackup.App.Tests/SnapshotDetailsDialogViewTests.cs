using System.Text;
using System.Windows;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The details dialog, forced through a real layout pass in all three themes - the same guard
/// <see cref="RestoreDialogViewTests"/> and <see cref="SettingsDialogViewTests"/> exist for, and
/// for the same reason: a StaticResource that cannot resolve throws during the pass, and a
/// DynamicResource that resolves to nothing leaves a dialog nobody notices is broken.
///
/// This one has two states that must both build - a configuration to describe, and a backup that
/// cannot be read - and the second is the one a damaged backup reaches.
/// </summary>
public sealed class SnapshotDetailsDialogViewTests
{
    private const string Rig = """
    {
      "MixerConfiguration": {
        "MixSettings": {
          "m1": {
            "Name": "Headphones", "IsMuted": false,
            "OutputDevices": [{ "Name": "Headphones", "FriendlyName": "Headphones (Elgato Wave:3)" }]
          },
          "m2": { "Name": "Stream Mix", "IsMuted": true, "OutputDevices": [] }
        },
        "MainOutputDeviceSettings": { "Name": "Headphones" },
        "InputSettings": {
          "a": {
            "InputName": "Wave Mic 1", "WaveDeviceType": "Wave3", "MixerIds": ["m1", "m2"],
            "AudioPluginConfigurations": [
              { "Name": "WaveCompressor", "Vendor": "Elgato", "Category": "Fx", "FilePath": "" },
              {
                "Name": "Pro-Q 4", "Vendor": "FabFilter", "Category": "EQ",
                "FilePath": "C:\\VST3\\Pro-Q 4.vst3", "BypassState": true
              }
            ]
          },
          "b": { "InputName": "Browser", "MixerIds": ["m1"] },
          "c": { "InputName": "Meld Studio", "MixerIds": [], "IsHiddenFromMixes": true }
        }
      }
    }
    """;

    private static Snapshot Snapshot() => new(
        "2026-08-20T1041-6b38a6",
        @"C:\store\2026-08-20T1041-6b38a6",
        new SnapshotManifest(
            SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
            DisplayName: "Full rig",
            Notes: string.Empty,
            CreatedUtc: new DateTimeOffset(2026, 8, 20, 8, 41, 0, TimeSpan.Zero),
            Trigger: SnapshotTrigger.Manual,
            SettingsSha256: new string('0', 64),
            WaveLinkVersion: "3.3.0.4108",
            InputCount: 3,
            InputNames: ["Wave Mic 1", "Browser", "Meld Studio"],
            EffectCount: 2,
            EffectChannelCount: 1,
            HasDuplicateKeys: false,
            Tiers: ["settings"],
            Files: new Dictionary<string, SnapshotFile>(StringComparer.Ordinal)
            {
                ["settings.json"] = new(new string('0', 64), 3_400_000),
            }));

    private static SnapshotDetailsModel Readable() => SnapshotDetailsModel.For(
        Snapshot(), ConfigurationDetail.Read(Encoding.UTF8.GetBytes(Rig)));

    private static SnapshotDetailsModel Unreadable() => SnapshotDetailsModel.For(
        Snapshot(),
        Result<ConfigurationDetail>.Fail(new SettingsUnreadable(@"C:\store\x", "the file is gone")));

    private static void ShowAndAssert(
        SnapshotDetailsModel model, AppTheme theme, Action<FrameworkElement> assert) => Wpf.Run(() =>
    {
        AppResources.Load(theme);

        var dialog = new SnapshotDetailsDialog(model)
        {
            Width = 1000,
            Height = 900,
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

    /// <summary>
    /// Only what a person can actually SEE. A collapsed element keeps its place in the visual tree
    /// and its text with it, so a check that ignored IsVisible would find the CHANNELS label on a
    /// dialog that is showing nothing but an error - which is exactly the claim the unreadable
    /// test below is making.
    /// </summary>
    private static void Collect(FrameworkElement element, List<string> text)
    {
        if (!element.IsVisible) return;

        switch (element)
        {
            case System.Windows.Controls.TextBlock { Text.Length: > 0 } block:
                text.Add(block.Text);
                break;
            case TrackedText { Text.Length: > 0 } tracked:
                text.Add(tracked.Text);
                break;
        }
    }

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.HighContrast)]
    public void The_dialog_renders_in_every_theme(AppTheme theme)
    {
        ShowAndAssert(Readable(), theme, _ => { });
    }

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.HighContrast)]
    public void The_unreadable_variant_renders_too(AppTheme theme)
    {
        ShowAndAssert(Unreadable(), theme, _ => { });
    }

    /// <summary>
    /// The channels, their effects, and the mixes all have to reach the visual tree - three nested
    /// ItemsControls, any of which could bind to nothing and leave a section that renders as a
    /// blank block. Read back from what was drawn rather than from the model.
    /// </summary>
    [Fact]
    public void Every_channel_its_chain_and_every_mix_are_drawn()
    {
        var text = new List<string>();

        ShowAndAssert(Readable(), AppTheme.Dark, e => Collect(e, text));

        // Every channel.
        Assert.Contains("Wave Mic 1", text);
        Assert.Contains("Browser", text);
        Assert.Contains("Meld Studio", text);

        // The chain, in order, with its meta.
        Assert.Contains("WaveCompressor", text);
        Assert.Contains("Pro-Q 4", text);
        Assert.Contains("01", text);
        Assert.Contains("02", text);
        Assert.Contains("FABFILTER · EQ · VST3", text);
        Assert.Contains("BYPASSED", text);

        // Routing, and the channel that is heard nowhere.
        Assert.Contains("IN HEADPHONES, STREAM MIX", text);
        Assert.Contains("NOT IN ANY MIX", text);
        Assert.Contains("HIDDEN", text);

        // The mixes and where they play out.
        Assert.Contains("Headphones", text);
        Assert.Contains("Stream Mix", text);
        Assert.Contains("HEADPHONES (ELGATO WAVE:3)", text);
        Assert.Contains("NO OUTPUT DEVICE", text);
        Assert.Contains("MUTED", text);
        Assert.Contains("WAVE LINK PLAYS OUT OF HEADPHONES", text);
    }

    /// <summary>
    /// The reason to open a DAMAGED backup at all: the dialog names it and says why it cannot
    /// describe it, rather than refusing or showing an empty shell.
    /// </summary>
    [Fact]
    public void The_unreadable_variant_shows_the_reason_and_no_empty_sections()
    {
        var text = new List<string>();

        ShowAndAssert(Unreadable(), AppTheme.Dark, e => Collect(e, text));

        Assert.Contains(text, t => t.Contains("the file is gone", StringComparison.Ordinal));
        Assert.Contains(text, t => t.Contains("What's in", StringComparison.Ordinal));
        Assert.DoesNotContain("CHANNELS", text);
    }

    /// <summary>
    /// Escape closes it, like every other dialog in the app - IsCancel on both Close buttons, so
    /// WPF does the work and there is no handler to get wrong.
    /// </summary>
    [Fact]
    public void Both_close_buttons_are_cancel_buttons()
    {
        var cancels = new List<string>();

        ShowAndAssert(Readable(), AppTheme.Dark, e =>
        {
            if (e is System.Windows.Controls.Button { IsCancel: true, Name.Length: > 0 } button)
            {
                cancels.Add(button.Name);
            }
        });

        Assert.Contains("CloseButton", cancels);
        Assert.Contains("FooterCloseButton", cancels);
    }
}
