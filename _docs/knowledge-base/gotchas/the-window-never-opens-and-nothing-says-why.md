---
title: "The window never opens and nothing says why"
status: published
created: 2026-08-19
updated: 2026-08-19
tags: [gotcha, wpf, dialogs, xaml, testing]
---

# The window never opens and nothing says why

**Provenance:** **Experienced**, three times in one session, from three different causes. The
first shipped in 0.5.0 and made the restore dialog unopenable for a whole phase. Now pinned by
`RestoreDialogViewTests` and `SettingsDialogViewTests`, which simply show each window, plus two
source scans in `TypographyTests` and `MainWindowTemplateTests`.

## Symptom

A dialog does not appear. Depending on where the caller sits, you get one of:

- an unhandled `XamlParseException` on the UI thread;
- a test runner reporting `[FATAL ERROR]` / "Catastrophic failure" and then hanging;
- in a release build with a `try`/`catch` upstream, **nothing at all**, the click does nothing.

The window is never partly built. It fails inside `InitializeComponent`, so there is no half-drawn
dialog to inspect and no binding-error trace to read.

## Cause

All three are **exceptions thrown while WPF applies a value during window construction.** That
is a different place from "a binding that resolves to nothing", which is why none of them produces
the binding-error output you would go looking for.

| What was written | What WPF does |
|---|---|
| A `Style` with `TargetType="TextBlock"` applied to a `TrackedText` | Throws `InvalidOperationException: 'TextBlock' TargetType does not match type of element 'TrackedText'` when the style is applied. |
| `<Run Text="{Binding SomeGetOnlyProperty}" />` | `Run.Text` is registered `BindsTwoWayByDefault`, a `Run` is editable inside a `RichTextBox`, so this throws *"A TwoWay or OneWayToSource binding cannot work on the read-only property"*. `TextBlock.Text` is one-way, so the same expression is fine there. |
| `<DoubleAnimation To="0" />` on `MaxHeight`, with no `From` | The animation reads the property's base value as its origin. `MaxHeight` defaults to `Infinity`, and `DoubleAnimation` throws *"cannot use default origin value of 'Infinity'"*. Fires on the state change, not at construction, here, on every **deselect**. |

The first is the nastiest, because the two names involved differ by four characters and describe
the same design role: `WlColumnHeaderText` (a `TextBlock` style) and `WlColumnHeaderTrackedText`
(the parallel `TrackedText` one), defined in two different files. Reaching for the wrong one reads
correctly at every glance.

## What does not fix it

| Attempt | Why it fails |
|---|---|
| Look for binding errors in the Output window | There are none. The value is never bound; applying it throws first. |
| Add a `try`/`catch` around `ShowDialog` | Turns a loud failure into a silent one. The dialog still does not open. |
| Assume the hang is a layout loop | It reads exactly like one, and that assumption cost a bisect. A throw on the WPF thread during construction presents as a window that never appears, with no message, in exactly the way an infinite measure pass does. |

## The fix

Per cause, one line each:

```xml
<!-- The parallel style for the element's real type -->
Style="{StaticResource WlColumnHeaderTrackedText}"

<!-- Run.Text is TwoWay by default; say otherwise -->
<Run Text="{Binding NoteOneLead, Mode=OneWay}" />

<!-- Give the animation an origin that is not Infinity -->
<Grid x:Name="ExpansionRow" MaxHeight="0" Opacity="0" ... />
```

## The real fix is the test

Every one of these was invisible to the compiler, to the XAML parser at build time, and to every
existing test, because **no test had ever constructed those two windows.** Model coverage was
thorough; view coverage was absent, and the failure mode of a view is that it does not exist.

Showing a window IS the assertion:

```csharp
var dialog = new RestoreDialog(model) { Left = -3000, Top = -3000 };
dialog.Show();
dialog.UpdateLayout();
```

Off-screen, in all three themes, then closed. Anything that throws during construction or layout
fails the test with the real exception attached. `RestoreDialogViewTests` and
`SettingsDialogViewTests` are eight cheap tests that between them would have caught all three.

The source scans are the second layer, and they catch the same class in windows nobody has written
a view test for yet:

- `TypographyTests.No_TrackedText_wears_a_TextBlock_style`
- `MainWindowTemplateTests.Every_Run_that_binds_its_text_asks_for_a_one_way_binding`

Both were verified to fail against the original code before the fix went in, a guard that cannot
fail is not a guard.

## See also

- [a-binding-expression-appears-on-screen.md](a-binding-expression-appears-on-screen.md), the
  same file, a different XAML rule, and a symptom you *can* see
- [a-dialog-opens-as-a-black-rectangle.md](a-dialog-opens-as-a-black-rectangle.md)
- The 0.5.1 design audit, 2026-08-19
