---
title: "A control in the Settings dialog moves and nothing happens"
status: published
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-004]
tags: [gotcha, wpf, settings]
---

# A control in the Settings dialog moves and nothing happens

**Provenance:** **Observed**, 2026-08-19, while adding two steppers to `WHEN TO BACK UP`. Two
separate defects with the same shape, found because the new controls would have landed in both.

## Symptom

Two flavours, and they look identical from the outside:

1. **The keep-count stepper's `−` and `+` do nothing.** The buttons render, they take focus,
   they animate on press, and the number beside them never moves. Shipped that way through
   phases 5 and 6.
2. **A toggle moves, and the app carries on as before.** Switch *Effect presets* off and the
   next capture still includes presets. Switch automatic backups off and the watcher keeps
   capturing. Restart the app and both are correct, the setting *was* saved.

The dialog's footer says `CHANGES APPLY AS YOU MAKE THEM`. For these controls it was not true.

## Cause

Two different breaks in the same chain, which is why one test suite could not see either.

**The stepper had no handler.** `DecrementKeepCountButton` and `IncrementKeepCountButton` were
declared in XAML, the readout was bound to `AutoBackupKeepCount`, and the view model clamped
and committed correctly when the property was set. Nothing ever set it. No `Click` was wired in
the code-behind and no command was bound, the control was complete at both ends and connected
in the middle to nothing.

**The commit reached disk and stopped there.** `App.BuildSettingsViewModel` passed
`s => repo.Save(s).IsSuccess` as the view model's save callback. That writes `settings.json`
correctly. It does not touch `App.settings`, the `BackupSettings` record that `GatherPayload`
closes over and that `BackupHost` was configured from at startup. So the file was right, every
object already running held the old value, and the change appeared on the next launch.

The comment above `Compose` said the closure existed *specifically* so a tier switched in the
dialog would take effect on the next capture rather than the next launch. The closure did.
The record it closed over never changed.

## The plausible explanation, and why it is wrong

> *"`SettingsViewModelTests` covers this property, it asserts the clamp, the commit, and the
> saved value. The behaviour is tested."*

It is, and the test is correct, and it passes for a control that is wired to nothing. The view
model was never the broken part. **A view-model property with a tested commit path is not
evidence that any control reaches it**, and a suite that only constructs view models cannot
distinguish "the dialog commits on change" from "the dialog would commit on change if anything
called this".

The second one is more tempting:

> *"The toggle is bound `TwoWay` and the setter commits. Binding is doing its job."*

Binding was doing its job. The setter ran, `Commit` ran, `repo.Save` ran and returned true. Every
link in the chain the code owns worked. The gap was one level up, in what the *host* did with a
successful save, and nothing in the dialog or its model can see that.

## Fix

Wire every stepper, and make the save callback the one place a settings change becomes true:

```csharp
// App.xaml.cs — written, held, and re-applied to the running host.
private bool ApplySettings(BackupSettings next)
{
    if (!settingsRepository!.Save(next).IsSuccess) return false;

    settings = next;

    if (host is not null)
    {
        host.AutoBackupEnabled = next.AutoBackupEnabled;
        host.Policy = AutoBackupPolicy.For(next);
    }

    return true;
}
```

`AutoBackupCoordinator.Policy` became settable for the same reason: a stepper whose new value
only applied on the next launch is a control that appears not to work.

## How to avoid it

- **Test the wiring, not only the model.** `SettingsDialogViewTests` now shows the real window,
  presses the `+` of every stepper, and asserts each value moved. **Only the `+` halves**. Press
  both and an unwired handler hides behind its opposite cancelling out, and the test passes
  vacuously. Verified by removing one handler and watching it fail.
- **A dialog with no Save button owes the user a definition of "commits".** Written to disk is
  half of it. If the running process holds a copy of what was written, the commit is not done
  until that copy is replaced.
- **Suspect anything whose only test constructs the object under test directly.** This is the
  same lesson [[a-dialog-opens-as-a-black-rectangle]] taught one layer down, the model was
  fine and the window could not open. These two suites now overlap on purpose.

## References

- [technical-debt.md](../../technical-debt.md) §4.20, both defects, with the phases they
  survived
- [[ADR-004]]: core library, thin shells: the shell owning the live settings record is what
  makes it the shell's job to re-apply them
- [[a-dialog-opens-as-a-black-rectangle]]: the same gap, one layer down
- [[a-binding-expression-appears-on-screen]]: the other failure a real layout pass catches
