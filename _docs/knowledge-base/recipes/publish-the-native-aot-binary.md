---
title: "Publish the NativeAOT binary"
status: published
created: 2026-08-17
updated: 2026-08-17
tags: [recipe, build, aot, toolchain]
---

# Publish the NativeAOT binary

**Why this exists:** [technical-debt.md](../../technical-debt.md) §2.4 keeps NativeAOT open for
the CLI, and `Core` carries `IsAotCompatible` plus a source guard against reflection-based JSON
to protect it. None of that is worth anything unless someone occasionally *checks*. This is how,
and why the check fails in a way that looks like your fault when it is not.

## AOT is not what a plain publish produces

`WaveLinkBackup.Cli.csproj` sets `PublishSelfContained` and `PublishSingleFile`, **not**
`PublishAot`. So:

```
dotnet publish src/WaveLinkBackup.Cli -c Release      →  ~70 MB   self-contained single-file
```

That is the shipped artifact. The 3.2 MB figure quoted in session records is a *different*
publish, run deliberately with the flag. Do not read 70 MB as a regression.

## The recipe

Two things are needed, and the second is the one that catches people.

1. A Visual Studio developer shell, for the MSVC linker.
2. **`vswhere.exe` on `PATH`** — the dev shell does *not* put it there.

```powershell
Import-Module 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
Enter-VsDevShell <instance-id> -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'

# The step the dev shell does not do for you:
$env:PATH = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer;' + $env:PATH

dotnet publish src/WaveLinkBackup.Cli -c Release `
  -p:PublishAot=true -p:PublishSingleFile=false -o "$env:TEMP\aot-check"
```

`PublishSingleFile=false` is required: it conflicts with AOT.

Expect **~3.2 MB**. Verify it runs — `wlbackup version` is enough to prove the native image
starts and the P/Invokes resolve.

## The failure signature, which lies

Without `vswhere.exe` on `PATH`:

```
error MSB3073: The command ""'vswhere.exe' is not recognized as an internal or external
command,;operable program or batch file.;C:\...\MSVC\14.44.35207\bin\Hostx64\x64\link.exe"
@"obj\...\native\link.rsp"" exited with code 123.
```

Read that carefully, because it is built to be misread:

- **It names `link.exe` and an exit code**, so it reads as a linker failure — a bad object file,
  an unresolved symbol, *something you just wrote*.
- The real cause is the first clause. `Microsoft.NETCore.Native.targets` shells out to
  `vswhere` to locate the toolchain, and when that fails **its error text is spliced into the
  command string** as if it were part of the path. The `;` are newlines.
- Managed compilation has already **succeeded** at this point. Any genuine AOT or trim problem
  in your code would have surfaced earlier as an `IL####` warning, not here.

So: if you add a `DllImport` and the AOT publish starts failing at the link step, check `PATH`
before you suspect the interop. The one time this happened here, the interop was fine.

A second red herring: the error may name a `link.exe` under
`C:\Program Files (x86)\...\BuildTools\...` even when the real install is Community. That is
just whichever install the targets found first, and it is not the problem either.

## Related

- [technical-debt.md](../../technical-debt.md) §2.4 — why AOT is kept open at all
- [guards-that-can-fail.md](../patterns/guards-that-can-fail.md) — the guards this recipe checks are still true
