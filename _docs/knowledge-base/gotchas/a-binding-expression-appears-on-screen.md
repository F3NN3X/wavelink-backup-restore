---
title: "A binding expression appears on screen"
status: published
created: 2026-08-19
updated: 2026-08-19
tags: [gotcha, wpf, xaml]
---

# A binding expression appears on screen

**Provenance:** **Experienced.** Reported by the user against the shipped 0.5.0 settings dialog,
which printed `{Binding WhatGoesIn.NoteOneLead}` and `{Binding WhatGoesIn.NoteOneRest}` under
WHAT GOES IN A BACKUP, and `{Binding WhereSettingsLive.FilePath}` further down. Now pinned by
`SettingsDialogViewTests.No_rendered_text_is_an_unevaluated_binding_expression` and by a source
scan in `MainWindowTemplateTests`.

## Symptom

The literal text `{Binding Something.Property}` renders in the UI, in the right font, in the right
place, where the value belongs.

It builds. It parses. There is no binding error, because there is no binding.

## Cause

**A markup extension is only evaluated in attribute syntax.** Written as a property element, the
braces are just characters:

```xml
<!-- Renders the value -->
<Run Text="{Binding NoteOneLead}" />

<!-- Renders the string "{Binding NoteOneLead}" -->
<Run><Run.Text>{Binding NoteOneLead}</Run.Text></Run>
```

XAML's parser treats the content of a property element as a value for that property, passed
through its type converter. `Text` is a `string`, the content is a valid `string`, and nothing
anywhere has cause to complain.

The property-element form gets reached for when the attribute form looks awkward, a long
expression, or several `Run`s that must not be separated by whitespace the parser will collapse.
Both are real problems with real solutions; this is not one of them.

## What does not fix it

| Attempt | Why it fails |
|---|---|
| Check the DataContext | It is correct. Nothing is being resolved, so nothing can resolve wrongly. |
| Look for a binding error in the Output window | A binding was never created. |
| `<Run.Text><Binding Path="X" /></Run.Text>` | This one actually **does** work, object-element syntax for the binding itself is evaluated. It is also three times the length, and it hides the same mistake next to it. |

## The fix

Attribute syntax, and a separate `Run` for any whitespace you need to keep:

```xml
<Run FontWeight="Medium" Foreground="{DynamicResource WlStrong}"
     Text="{Binding NoteOneLead, Mode=OneWay}" /><Run Text=" " /><Run
     Foreground="{DynamicResource WlMuted}"
     Text="{Binding NoteOneRest, Mode=OneWay}" />
```

Two details that are not optional:

- **The space is its own `Run`.** A `Run`'s leading whitespace is collapsed away, so it cannot
  ride on the front of the second one.
- **`Mode=OneWay`.** `Run.Text` binds TwoWay by default and every model property behind one of
  these is get-only, so the plain form throws. See
  [the-window-never-opens-and-nothing-says-why.md](the-window-never-opens-and-nothing-says-why.md).
  Fixing this gotcha lands you directly in that one.

## Why it survived a phase

The settings dialog's *model* was covered thoroughly, `SettingsViewModelTests` drives every
control's commit behaviour against a real repository. The **view** was never constructed by any
test, so nothing ever looked at what it rendered.

`SettingsDialogViewTests` now reads the `Run`s the dialog actually produced and fails on any text
containing `{Binding`, the rendered string, not the source spelling.

## See also

- `src/WaveLinkBackup.App/Views/SettingsDialog.xaml`, the two plain-language notes
- [the-window-never-opens-and-nothing-says-why.md](the-window-never-opens-and-nothing-says-why.md)
- [2026-08-19-design-audit-and-ui-fixes.md](../../sessions/2026-08-19-design-audit-and-ui-fixes.md)
