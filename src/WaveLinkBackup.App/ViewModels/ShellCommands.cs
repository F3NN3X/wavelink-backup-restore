using System.Windows.Input;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// Screen 1's key map, as data.
///
/// RoutedUICommand rather than an ICommand per action: the Text property is what a screen
/// reader announces and what a context menu would show, and it comes free. 7.4 is explicit that
/// reader labels are part of this work rather than a follow-up.
/// </summary>
public static class ShellCommands
{
    public static RoutedUICommand Refresh { get; } =
        New("Re-read the backup folder", nameof(Refresh), Key.F5, ModifierKeys.None);

    public static RoutedUICommand Search { get; } =
        New("Search backups", nameof(Search), Key.F, ModifierKeys.Control);

    public static RoutedUICommand ClearSearch { get; } =
        New("Clear the search", nameof(ClearSearch), Key.Escape, ModifierKeys.None);

    public static RoutedUICommand BackUpNow { get; } =
        New("Back up now", nameof(BackUpNow), Key.B, ModifierKeys.Control);

    public static RoutedUICommand Rename { get; } =
        New("Rename this backup", nameof(Rename), Key.F2, ModifierKeys.None);

    public static RoutedUICommand Delete { get; } =
        New("Delete this backup", nameof(Delete), Key.Delete, ModifierKeys.None);

    public static RoutedUICommand Restore { get; } =
        New("Restore this backup", nameof(Restore), Key.Enter, ModifierKeys.None);

    public static IReadOnlyList<RoutedUICommand> All { get; } =
        [Refresh, Search, ClearSearch, BackUpNow, Rename, Delete, Restore];

    private static RoutedUICommand New(string text, string name, Key key, ModifierKeys modifiers) =>
        new(text, name, typeof(ShellCommands), [new KeyGesture(key, modifiers)]);
}
