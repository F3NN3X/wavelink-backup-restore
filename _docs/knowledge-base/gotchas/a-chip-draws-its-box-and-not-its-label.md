---
title: "A chip draws its box and not its label"
status: published
created: 2026-08-20
updated: 2026-08-20
related_adrs: [ADR-005]
tags: [gotcha, wpf, xaml]
---

# A chip draws its box and not its label

**Provenance:** Observed, 2026-08-20, on the CONTENTS column of every row in the shipped app,
found by comparing a screenshot against the design's own render, not by a failing test.

## Symptom

The CONTENTS column draws three small pills per row, correctly shaped and correctly styled, the
present ones filled with a hairline border, the absent ones a dashed ghost at 50%, and **all of
them empty**. `SETTINGS`, `PRESETS` and `PLUGINS` are nowhere on screen.

Nothing throws. No binding error appears in the output window. The column looks like a deliberate
minimal treatment rather than a defect, which is how it survived a design conformance audit that
read every XAML file in the app.

## Cause

```xml
<ContentControl Focusable="False">           <!-- no Content -->
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Setter Property="ContentTemplate" Value="{StaticResource WlTierAbsent}" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsPresent}" Value="True">   <!-- reads DataContext -->
                    <Setter Property="ContentTemplate" Value="{StaticResource WlTierPresent}" />
```

**A `ContentTemplate` renders against `Content`. The triggers around it read `DataContext`.** The
two are different, and only one of them was set.

So the trigger picked the right template from the real tier data, which is why the present and
absent shapes were right, and then that template's `{Binding Label}` resolved against a null
`Content` and produced nothing. The box is drawn by the template; the label comes from the data
the template never got.

## The plausible explanation, and why it is wrong

*"The `Label` property must be empty — check the view model."* It is not: `TierBadge` carries
`SETTINGS`/`PRESETS`/`PLUGINS`, and its tests pass. The binding is fine, the property is fine, the
template is fine, and the style is fine. Nothing is wrong with any piece; the piece that is missing
is the one attribute that connects them.

The second trap is subtler and cost this project a real audit: **every source-text guard in the
file still passed.** `RowTemplateTests` asserted the templates exist, that a damaged row forces the
ghost, that the brushes are theme keys, all reading the markup, all of which was present and
correct. A defect that removes *rendered output* while leaving *markup* intact is invisible to
every test that reads the file rather than the tree.

## Fix

```xml
<ContentControl Content="{Binding}" Focusable="False">
```

The same idiom `WlSlotTemplate` and the WHY/INPUTS cells already used, which is what made the
omission easy to miss when reading the file: the surrounding code is right.

## How to avoid it

**Assert on the rendered tree, not on the markup.** The guard that catches this instantiates the
real row template in a real window and reads the labels back out:

`tests/…/RowTemplateTests.cs::The_contents_column_renders_all_three_tier_labels`

Verified to fail against the old markup, it reported `["MANUAL", "MIC 1", "VOICE", …]` with no
tier labels at all, which is the check that makes a new guard worth having.

**And then the labels did not fit.** With the text rendering for the first time, the third badge
read `PLUGIN`, clipped mid-word: three badges measure 224.2px at the design's own type role and
padding, in a column the design gives 200. Its own reference render draws them at about 224 too, so
the arithmetic in the spec and the picture beside it disagree. The column is 248 now, and the
guard that holds it is a *measurement*,
`The_three_tier_badges_fit_inside_the_contents_column` renders the badges and compares their width
against the column, so a change to the font, the size, the tracking or the padding fails there
rather than on someone's screen.

## References

- `src/WaveLinkBackup.App/Views/RowStyles.xaml`, the tier badge templates
- [[a-binding-expression-appears-on-screen]]: the other "it renders, and it is only wrong to look
  at" XAML trap in this project
- `_docs/audits/2026-08-19-design-conformance.md`, the audit that read this file and did not see it
