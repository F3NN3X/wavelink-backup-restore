# Third-party notices

Everything in this repository that somebody else wrote, and the licence it comes under.

---

## Lucide

**Used for:** the icon set. Every glyph in the app is a Lucide path, copied verbatim onto the same
24px grid — the eleven `README.md` §icons names (`shield-check`, `download`, `rotate-ccw`,
`pencil`, `trash-2`, `search`, `settings`, `folder`, `triangle-alert`, `circle-check`,
`chevron-down`) plus `check`, `x` and `circle-slash`.

**Where:** `src/WaveLinkBackup.App/Views/ControlStyles.xaml`, `Views/RowStyles.xaml`,
`Views/TrayMenuStyles.xaml` and `Views/TrayIconRenderer.cs`. Each carries a comment naming the
Lucide icon it came from.

**Two mechanical changes, and no others.** Lucide draws several glyphs with `<circle>`, which the
WPF path mini-language has no element for, so each is written as the two half-arcs describing the
same circle with the original `cx`/`cy`/`r` named in a comment. And icons Lucide draws as several
`<path>` elements are concatenated into one `Geometry`, which is what a single WPF `Path` renders.

The **stroke weight is 1.75px, not Lucide's 2px** — this app's design fixes that weight
(`README.md`, "Space, shape, motion"), and it is the one figure in the icon work that is ours
rather than theirs.

**Source:** <https://github.com/lucide-icons/lucide>

```
ISC License

Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT).
All other copyright (c) for Lucide are held by Lucide Contributors 2022.

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

---

## H.NotifyIcon

**Used for:** the tray icon and its context menu (`src/WaveLinkBackup.App`). MIT.

**Source:** <https://github.com/HavenDV/H.NotifyIcon>

---

## voltybat/WaveLinkSettingsUtility

**Not vendored.** This project was informed by reading it — see
[`_docs/audits/2026-08-15-voltybat-wavelinksettingsutility.md`](_docs/audits/2026-08-15-voltybat-wavelinksettingsutility.md)
— but no code was copied. The five defects that audit found are recorded in
[`_docs/technical-debt.md`](_docs/technical-debt.md) §1, and several are worth offering back
upstream.
