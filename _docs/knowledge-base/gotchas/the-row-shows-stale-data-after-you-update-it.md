---
title: "The row shows stale data after you update it"
status: published
created: 2026-08-24
updated: 2026-08-24
tags: [gotcha, wpf, binding, viewmodel]
---

# The row shows stale data after you update it

**Provenance:** **Experienced.** Reported by the user against the settings dialog's trash row:
after emptying the trash, the row kept showing the old count and size instead of refreshing. Now
pinned by the `TrashRow` property being a notifying property in `SettingsViewModel`, and by the
post-empty refresh path in `App.EmptyTrash`.

## Symptom

A row or cell in the UI keeps showing its **old** value even though the view model's property was
re-assigned to a new object. The data is correct *in memory*, dump the view model and the new
value is there, but the screen never updates. Reopening the window (or triggering some unrelated
refresh) makes it right, which is what makes this one look like a data bug instead of a binding bug.

## Cause

The property on the view model was an **auto-property**:

```csharp
public TrashRowModel? TrashRow { get; set; }   // never raises PropertyChanged
```

WPF's binding engine only redraws when it hears `PropertyChanged` for that property name. An
auto-property's setter is a plain field write, it raises nothing. So `TrashRow = newValue` updates
the backing field and the screen goes on showing whatever was bound before. The first bind happened
to coincide with the window opening, which is why the value looked right at first and wrong after
every later re-assignment.

## The plausible explanation, and why it is wrong

**"The new object must be stale / the refresh isn't computing the right count."** It is not. The
refresh *is* producing a correct `TrashRowModel`; you can see it if you log or inspect the view
model after the operation. The value that is wrong is the one **on screen**, and the reason is not
that the new value is bad but that the binding never heard about it. Chasing the data, re-reading
the trash, recomputing the size, doubting the count, all confirms the data is fine and sends you in
circles. The defect is one line away, in *how the property is declared*, not in what gets assigned.

## Fix

Make the property notify, using the view model's existing `Set` helper (the same one every other
observable property in the class uses):

```csharp
private TrashRowModel? trashRow;
public TrashRowModel? TrashRow
{
    get => trashRow;
    set => Set(ref trashRow, value);   // raises PropertyChanged(nameof(TrashRow))
}
```

Every write site, initial open, folder change, post-empty refresh, now goes through a setter that
the binding engine can hear. No XAML changes: the bindings were already correct; they just had no
event to fire on.

## How to avoid it

**A view-model property that gets re-assigned after construction must be a notifying property, not
an auto-property.** The tell is "I set this again later and the UI didn't move." In this codebase
the guard is the convention: `SettingsViewModel` (and its peers) route every observable property
through `ObservableObject.Set(ref …)`. An auto-property on a view model is a smell by itself, if a
property is only ever set once in the constructor, an auto-property is fine; the moment there is a
second write site, it has to notify.

A test that constructs the dialog, performs the operation that re-assigns the property, and asserts
the **rendered** text changed (not just that the view model's value changed) is what holds this down:
it fails on an auto-property because the screen never updates, even though the in-memory value is
correct.

## References

- `src/WaveLinkBackup.App/ViewModels/SettingsViewModel.cs`, the `TrashRow` property and its three
  write sites
- `src/WaveLinkBackup.App/App.xaml.cs`, `EmptyTrash`, the post-empty refresh that re-assigns it
- [[ADR-004]]: the shells stay thin; the view model is where the observable state lives, which is
  exactly why a silent setter there has nowhere else to be caught
