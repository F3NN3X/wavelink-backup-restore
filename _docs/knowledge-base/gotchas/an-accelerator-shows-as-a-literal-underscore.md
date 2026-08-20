---
title: "An accelerator shows as a literal underscore"
status: published
created: 2026-08-20
updated: 2026-08-20
tags: [gotcha, wpf, accessibility, xaml]
---

# An accelerator shows as a literal underscore

**Provenance:** *Observed*, 2026-08-20, while closing
[technical-debt.md](../../technical-debt.md) §7.4's "Alt-accelerators on dialog buttons". Caught
before it shipped only because the fix was written with a test beside it.

## Symptom

You add an access key the normal way — `Content="_Cancel"` — and the button renders **`_Cancel`**,
with the underscore as text. `Alt+C` does nothing. Every other button in the app behaves the same
way, so it reads as "WPF accelerators are broken here" rather than as a bug in one file.

## Cause

`ContentPresenter.RecognizesAccessKey` defaults to **`false`**.

WPF's *stock* Button template sets it to `true`, which is why the feature appears to work
everywhere until you have your own `ControlTemplate`. Every button in this app is templated
(`WlGhostButton` and the six styles based on it), and each declared a bare:

```xml
<ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
```

so the underscore was never interpreted.

## The plausible explanation, and why it is wrong

The first guess is **"Windows hides accelerator underlines until Alt is pressed"** — which is true,
and is exactly the wrong lead. That behaviour hides the *underline* on a working accelerator; it
does not print an underscore. If you can see an underscore character, the access key was never
parsed, and pressing Alt will not reveal anything.

The second guess is that `AccessText` is needed instead of a plain string. It is needed when the
content is an *element* rather than a string — this app's destructive buttons hold a `StackPanel`
with an icon, so those really do need `<AccessText Text="_Restore this backup" />`. But swapping a
plain `Content="_Cancel"` for an `AccessText` changes nothing while the presenter still refuses to
recognise it. Both halves are required, and fixing only the visible one sends you looking in the
wrong file.

## Fix

Set it on every templated button's presenter:

```xml
<ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                  RecognizesAccessKey="True" />
```

Where the content is an element, use `AccessText` rather than `TextBlock` for the label inside it.

## How to avoid it

**Count presenters against recognisers**, rather than trusting that the next templated button
remembers. `KeyboardConventionTests.Every_button_template_recognises_an_access_key` does exactly
that, and fails the build if a new `ControlTemplate` adds a bare `ContentPresenter`.

This is the same shape as
[[a-settings-control-moves-and-nothing-happens]]: a control that **looks** wired, with a model
behind it that is correct, and nothing joining the two. The general lesson is worth keeping — *a
declared affordance is not evidence of a working one* — and it is why the accessibility work got
view tests rather than model tests.

## References

- [[a-settings-control-moves-and-nothing-happens]] — the same failure shape, a stepper with no handler
- [technical-debt.md](../../technical-debt.md) §7.4 — Windows keyboard conventions
- `tests/WaveLinkBackup.App.Tests/KeyboardConventionTests.cs`
- `src/WaveLinkBackup.App/Views/ControlStyles.xaml`
