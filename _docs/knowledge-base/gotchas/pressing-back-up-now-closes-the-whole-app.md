---
title: "Pressing Back up now closes the whole app"
status: published
created: 2026-08-20
updated: 2026-08-20
tags: [gotcha, wpf, threading]
---

# Pressing Back up now closes the whole app

**Provenance:** Observed, 2026-08-20. Reported as *"creating backups crash the app"*; confirmed
from the Windows Application event log, which carried the exception and the stack three times over.

## Symptom

Press **Back up now** in the window. The app disappears, window, tray icon, the lot. No message,
no dialog, no log of its own. The backup itself is written and is perfectly good; the process that
wrote it is gone.

The tray menu's own *Back up now* works fine, which is what makes it look like a data problem: the
same capture succeeds from one entry point and kills the app from the other.

## Cause

```
System.InvalidOperationException: The calling thread cannot access this object because a
different thread owns it.
   at H.NotifyIcon.TaskbarIcon.set_Icon(Icon value)
   at WaveLinkBackup.App.App.RefreshTray()          App.xaml.cs:1208
   at WaveLinkBackup.App.App.BackUpNow(IProgress`1)  App.xaml.cs:425
   at MainWindow.<BackUpNowAsync>b__0()              MainWindow.xaml.cs:681
```

`BackUpNow` refreshes the tray icon, its tooltip and three menu items after a capture. Every caller
ran on the UI thread until the backing-up progress strip was built, which moved the capture into a
`Task.Run` so the bar could animate:

```csharp
result = await Task.Run(() => app.BackUpNow(progress));   // <- the refresh now runs here too
```

`TaskbarIcon.Icon` is a `DependencyProperty`. Writing one from a thread that does not own the
object throws, and an exception on a thread-pool thread, rethrown into an `async void` event
handler with nothing above it, ends the process.

**The tray menu's entry point is unaffected because it never left the UI thread.**

## The plausible explanation, and why it is wrong

*"It started when I added channels to Wave Link, so the capture is choking on the bigger
configuration."* That is what it looks like, the crash appears the day the rig changes, and it
appears on the action that reads the rig.

It is not that. `wlbackup backup` captured all nine channels from the command line without
complaint, which rules Core out in one command and is worth doing first. The window's button had
been fatal since the progress strip landed; adding channels is only what made someone press it.

The second plausible explanation is more expensive: *"WPF marshals property changes for bindings,
so cross-thread view-model updates are fine."* True for a bound property on an `INotifyPropertyChanged`
object, and irrelevant here. This is a `DependencyObject` being written directly, and `SetValue`
calls `VerifyAccess` before any binding machinery is involved.

## Fix

Marshal at the METHOD, not at the call site:

```csharp
private void RefreshTray()
{
    if (UiThread.Marshal(Dispatcher, RefreshTray)) return;   // ran there instead; we are done
    ...
}
```

`UiThread.Marshal` returns true when it has run the work on the dispatcher's thread, so a method
guards itself in one line. `RefreshShellFacts` carries the same guard, for the same reason.

## How to avoid it

**Guard the method, not the caller.** Fixing the one caller fixes one caller, and this bug arrived
exactly that way: the same hazard was spotted for `SystemEvents.DisplaySettingsChanged` a phase
earlier and handled with a `Dispatcher.BeginInvoke` at that call site, and then missed here.

`UiThreadTests` reproduces the failure directly: it writes a `DependencyProperty` from the test
thread, asserts the throw, then writes the same property through the guard and asserts it lands.

**And note what has no guard at all:** this app installs no
`Application.DispatcherUnhandledException` handler, so any stray exception still ends the process
with no trace but the Windows event log. See `technical-debt.md` §8.1.

## References

- `src/WaveLinkBackup.App/Hosting/UiThread.cs` · `tests/…/UiThreadTests.cs`
- [[the-tray-icon-refuses-every-image-you-draw]]: the other tray-icon trap, same file
- `_docs/technical-debt.md` §8.1, the missing crash surface
