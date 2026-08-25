---
title: "COM interop stops compiling the moment the project is AOT-compatible"
status: published
created: 2026-08-25
updated: 2026-08-25
related_adrs: [ADR-017]
tags: [gotcha, interop, aot, build]
---

# COM interop stops compiling the moment the project is AOT-compatible

**Provenance:** **Observed**, 2026-08-25, porting `WindowsAudioEndpointInspector` into
`WaveLinkBackup.Core`. Both errors below were produced on this machine, in that order, and the
working third form was verified by an AOT publish that ran.

## Symptom

You paste a perfectly ordinary Core Audio interop class — the one every sample on the internet
shows, the one upstream ships — into a library, and it will not build. Not a warning. An error,
from a file you did not write:

```
error IL2072: 'type' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicParameterlessConstructor'
in call to 'System.Activator.CreateInstance(Type)'. The return value of method
'System.Type.GetTypeFromCLSID(Guid)' does not have matching annotations.
```

Rewrite the activation to avoid reflection and you get a different error from the same family:

```
error IL2050: P/invoke method 'CoCreateInstance(ref Guid, nint, UInt32, ref Guid, out Object)'
declares a parameter with COM marshalling. Correctness of COM interop cannot be guaranteed after
trimming. Interfaces and interface members might be removed.
```

The same code in a console app compiles without a murmur.

## Cause

`<IsAotCompatible>true</IsAotCompatible>` turns on trim and AOT analysers, and
`<TreatWarningsAsErrors>` promotes what they find. Both messages are about the same thing from two
angles: **the trimmer cannot see through COM.**

- A CLSID is a runtime value. `Type.GetTypeFromCLSID` hands back a `Type` the trimmer has no way to
  connect to any type it can keep, so it cannot promise the constructor still exists.
- Built-in COM marshalling builds its vtable dispatch by reflecting over the interface at runtime.
  The trimmer cannot prove which members are reachable, so it cannot promise it has kept them.

Neither is a false positive. Trimming really can remove those members, and the result is not a
build failure — it is a `NullReferenceException` or a wrong-slot call in a shipped binary.

## The plausible explanation, and why it is wrong

**"It is a warning about trimming, and I am not trimming — suppress it."** This is the wrong turn,
and it is the attractive one because the code demonstrably works when you run it from `bin/Debug`.

It is wrong twice. The flag is on because *something else* wants it: here, the CLI's NativeAOT
option, kept open since phase 1. Suppressing the message in the library silently revokes that for
the whole solution, and nobody finds out until a publish months later. And the analyser is not
speculating — it is describing a real mechanism, so the suppression converts a build error into a
crash on a user's machine.

The second attractive wrong turn is **"drop `IsAotCompatible` from this project."** Same problem
wearing a different hat, and it also throws away a setting that has caught real issues in code that
has nothing to do with COM.

## Fix

Use source-generated COM. `[GeneratedComInterface]` emits the marshalling at compile time, so
there is nothing left for the trimmer to lose.

Three things make it work:

1. **`[GeneratedComInterface]` instead of `[ComImport]`**, and the interfaces must be `partial` —
   as must any type they are nested in.
2. **Blittable parameters only.** Declare interface out-parameters as `IntPtr` and wrap them by
   hand rather than letting the marshaller do it; that keeps the generated code trivial and
   sidesteps IL2050 entirely.
3. **`CoCreateInstance` with an `out IntPtr`**, then
   `StrategyBasedComWrappers.GetOrCreateObjectForComInstance`. An
   `[MarshalAs(UnmanagedType.Interface)]` out-parameter here is what produces IL2050 in the first
   place.

```csharp
[GeneratedComInterface]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IntPtr device);   // IntPtr, not IMMDevice
}
```

The generator needs `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` — see [[ADR-017]], which is also
where the trade against `RecycleBin`'s deliberate refusal of that flag is argued.

**Two smaller traps on the way.** Structs used in generated signatures must be at least `internal`,
or the generated file cannot see them and you get a cascade of CS0122 followed by confusing CS8500
"pointer to a managed type" errors — the second is fallout from the first, not a separate problem.
And a `[SupportedOSPlatform("windows")]` annotation is needed on the class when the project targets
plain `net10.0`, or CA1416 becomes the next build error.

## How to avoid it

Nothing prevents this one — it is a property of the analysers, and hitting it is how you learn
which mechanism to reach for. What makes it cheap the second time is knowing the answer is
"source-generated COM", not "suppress it".

The related guard worth having is the one this project *did* add afterwards for a different
mistake: `SourceGuardTests` had enforced the file-lock rule in C# since phase 1 and only ever
scanned `*.cs`, so the first PowerShell tool repeated it exactly. `ToolScriptGuardTests` now
extends that rule to `tools/*.ps1`. **A guard that covers one language covers one language**, and
the second language arrives without announcing itself.

## References

- [[ADR-017]] — the decision, and why `AllowUnsafeBlocks` on Core is a different trade from the one
  `RecycleBin` refused
- [technical-debt.md](../../technical-debt.md) §2.4 — the question this closed, and its evidence
  table in [the archive](../../archive/technical-debt-closed.md)
- `src/WaveLinkBackup.Core/Abstractions/WindowsAudioEndpointInspector.cs` — the working form
