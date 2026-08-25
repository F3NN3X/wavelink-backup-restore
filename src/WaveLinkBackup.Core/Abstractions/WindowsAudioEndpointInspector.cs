using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace WaveLinkBackup.Core.Abstractions;

/// <summary>
/// The real audio stack, via Core Audio's <c>IMMDeviceEnumerator</c>.
///
/// <para>
/// LIVES IN CORE, for the reason <see cref="RecycleBin"/> already established: interop needs
/// nothing from the Windows Desktop ref pack, and <c>GuardNoDesktopFramework</c> guards the ref
/// pack rather than interop. Core is Windows-only in behaviour (ADR-008) and headless in
/// dependencies; this is the third such class, not the first.
/// </para>
///
/// <para>
/// <b>Marked <see cref="SupportedOSPlatformAttribute"/> rather than left bare.</b> Core targets
/// plain <c>net10.0</c>, so this says what the target framework cannot: the type is Windows only,
/// and a caller on another platform is the caller's bug rather than a silent failure.
/// </para>
///
/// <para>
/// <b>This is the answer to technical-debt.md 2.4, and the answer is "not the way upstream does
/// it".</b> Two mechanisms were tried against <c>IsAotCompatible</c> and both failed to build:
/// </para>
/// <list type="number">
/// <item>
/// <c>Activator.CreateInstance(Type.GetTypeFromCLSID(clsid))</c> - <b>IL2072</b>. A CLSID resolved
/// at runtime yields a <see cref="Type"/> the trimmer cannot prove has a parameterless
/// constructor. This is upstream's activation path.
/// </item>
/// <item>
/// Classic <c>[ComImport]</c> interfaces reached through a <c>CoCreateInstance</c> P/Invoke
/// declaring <c>[MarshalAs(UnmanagedType.Interface)]</c> - <b>IL2050</b>. Built-in COM marshalling
/// cannot be verified after trimming, because the interfaces and their members might be removed.
/// </item>
/// </list>
/// <para>
/// What DOES survive is source-generated COM: <see cref="GeneratedComInterfaceAttribute"/> emits
/// the vtable marshalling at compile time, so there is nothing for the trimmer to lose. Every COM
/// method below therefore takes blittable parameters only - interface out-parameters are raw
/// pointers wrapped by hand through <see cref="StrategyBasedComWrappers"/> - which keeps the
/// generated marshalling trivial and the AOT publish clean.
/// </para>
/// <para>
/// The cost is <c>AllowUnsafeBlocks</c> on Core, which <see cref="RecycleBin"/> deliberately
/// refused for its one <c>DllImport</c>. That refusal still stands on its own terms: there, unsafe
/// bought marshalling speed on a call that happens once a session. Here it is not an optimisation
/// but the only mechanism that compiles at all, which is a different trade with a different answer.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsAudioEndpointInspector : IAudioEndpointInspector
{
    // BCDE0395-E52F-467C-8E3D-C4579291692E
    private static readonly Guid ClsidMMDeviceEnumerator =
        new(0xBCDE0395, 0xE52F, 0x467C, 0x8E, 0x3D, 0xC4, 0x57, 0x92, 0x91, 0x69, 0x2E);

    // A95664D2-9614-4F35-A746-DE8DB63617E6, as a value: CoCreateInstance takes the IID by reference.
    private static readonly Guid IidMMDeviceEnumerator =
        new(0xA95664D2, 0x9614, 0x4F35, 0xA7, 0x46, 0xDE, 0x8D, 0xB6, 0x36, 0x17, 0xE6);

    /// <summary>CLSCTX_INPROC_SERVER. MMDeviceEnumerator is in-proc; nothing here wants a surrogate.</summary>
    private const uint ClsCtxInprocServer = 0x1;

    private const int EDataFlowRender = 0;
    private const int EDataFlowCapture = 1;

    /// <summary>Every state, including the dead ones - which are the interesting ones here.</summary>
    private const uint DeviceStateMaskAll = 0x0000000F;

    private const uint DeviceStateActive = 0x00000001;
    private const uint DeviceStateDisabled = 0x00000002;
    private const uint DeviceStateNotPresent = 0x00000004;
    private const uint DeviceStateUnplugged = 0x00000008;

    /// <summary>STGM_READ. Nothing here writes.</summary>
    private const uint StgmRead = 0x00000000;

    /// <summary>VT_LPWSTR - the only variant type a friendly name arrives as.</summary>
    private const ushort VtLpwstr = 31;

    // PKEY_Device_FriendlyName: {a45c254e-df1c-4efd-8020-67d146a850e0}, 14
    private static readonly PropertyKey PkeyDeviceFriendlyName = new(
        new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
        14);

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    public IReadOnlyList<AudioEndpoint> List()
    {
        // A machine with no audio service, or none with sound hardware at all, is a real state
        // rather than an error: the caller asked whether a channel's device is alive, and "no"
        // is a complete answer. Throwing here would fail a capture that has nothing to do with
        // endpoints.
        var clsid = ClsidMMDeviceEnumerator;
        var iid = IidMMDeviceEnumerator;

        var hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsCtxInprocServer, ref iid, out var raw);
        if (hr < 0 || raw == IntPtr.Zero) return [];

        var enumerator = Wrap<IMMDeviceEnumerator>(raw);
        if (enumerator is null) return [];

        try
        {
            return
            [
                .. Collect(enumerator, EDataFlowCapture, EndpointDirection.Capture),
                .. Collect(enumerator, EDataFlowRender, EndpointDirection.Render),
            ];
        }
        catch (COMException)
        {
            return [];
        }
    }

    /// <summary>
    /// Takes ownership of <paramref name="pointer"/>: the wrapper adds its own reference, so the
    /// one CoCreateInstance handed us is ours to release either way.
    /// </summary>
    private static T? Wrap<T>(IntPtr pointer) where T : class
    {
        if (pointer == IntPtr.Zero) return null;

        try
        {
            return ComWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None) as T;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static List<AudioEndpoint> Collect(
        IMMDeviceEnumerator enumerator, int dataFlow, EndpointDirection direction)
    {
        var found = new List<AudioEndpoint>();

        if (enumerator.EnumAudioEndpoints(dataFlow, DeviceStateMaskAll, out var collectionPointer) < 0)
        {
            return found;
        }

        var collection = Wrap<IMMDeviceCollection>(collectionPointer);
        if (collection is null) return found;

        if (collection.GetCount(out var count) < 0) return found;

        for (var i = 0u; i < count; i++)
        {
            if (collection.Item(i, out var devicePointer) < 0) continue;

            var device = Wrap<IMMDevice>(devicePointer);
            if (device is null) continue;

            try
            {
                found.Add(Describe(device, direction));
            }
            catch (COMException)
            {
                // One endpoint that will not describe itself is not a reason to lose the other
                // eleven. A device disappearing mid-enumeration is the ordinary case.
            }
        }

        return found;
    }

    private static AudioEndpoint Describe(IMMDevice device, EndpointDirection direction)
    {
        var id = device.GetId(out var idPointer) < 0 ? "" : PtrToStringAndFree(idPointer);
        var state = device.GetState(out var raw) < 0 ? uint.MaxValue : raw;

        return new AudioEndpoint(id, ReadFriendlyName(device), direction, ToEndpointState(state));
    }

    /// <summary>
    /// The name a person would recognise. An endpoint that will not surrender one still counts -
    /// its id is what a channel key matches on, and a nameless live device beats a dropped row.
    /// </summary>
    private static string ReadFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(StgmRead, out var storePointer) < 0) return "";

        var store = Wrap<IPropertyStore>(storePointer);
        if (store is null) return "";

        var key = PkeyDeviceFriendlyName;
        if (store.GetValue(ref key, out var value) < 0) return "";

        try
        {
            return value.Type == VtLpwstr && value.Data != IntPtr.Zero
                ? Marshal.PtrToStringUni(value.Data) ?? ""
                : "";
        }
        finally
        {
            // PropVariantClear owns the string; freeing the pointer by hand would release memory
            // the variant still believes it holds.
            PropVariantClear(ref value);
        }
    }

    /// <summary>
    /// COM allocated it, so the caller frees it. Marshalling the parameter as a string instead
    /// would have the runtime copy the buffer and then leak the original.
    /// </summary>
    private static string PtrToStringAndFree(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return "";

        try
        {
            return Marshal.PtrToStringUni(pointer) ?? "";
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static EndpointState ToEndpointState(uint state) => state switch
    {
        DeviceStateActive => EndpointState.Active,
        DeviceStateDisabled => EndpointState.Disabled,
        DeviceStateNotPresent => EndpointState.NotPresent,
        DeviceStateUnplugged => EndpointState.Unplugged,
        _ => EndpointState.Unknown,
    };

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    /// <summary>
    /// Every parameter blittable, including <paramref name="instance"/> - an
    /// <c>[MarshalAs(UnmanagedType.Interface)]</c> out-parameter here is what produced IL2050.
    /// </summary>
    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    /// <summary>
    /// Only as much PROPVARIANT as a friendly name needs. The real union is larger; the padding
    /// keeps the struct the size the callee expects, and reading past <see cref="Data"/> is what
    /// a fuller declaration would be for.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Data;
        public IntPtr Padding;
    }

    // The vtable order below is the interface's, not a convenience ordering: a method left out or
    // moved shifts every slot after it, and the failure is a call into the wrong function rather
    // than a compile error. Unused methods are declared for their slot alone.

    [GeneratedComInterface]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    internal partial interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr device);

        [PreserveSig]
        int GetDevice(IntPtr id, out IntPtr device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [GeneratedComInterface]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    internal partial interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint index, out IntPtr device);
    }

    [GeneratedComInterface]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    internal partial interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint classContext, IntPtr activationParams, out IntPtr instance);

        [PreserveSig]
        int OpenPropertyStore(uint access, out IntPtr store);

        [PreserveSig]
        int GetId(out IntPtr id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [GeneratedComInterface]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    internal partial interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);

        [PreserveSig]
        int Commit();
    }
}
