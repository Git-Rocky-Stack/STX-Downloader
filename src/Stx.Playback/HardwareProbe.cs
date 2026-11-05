using System.Runtime.InteropServices;
using Stx.Core.Playback;

namespace Stx.Playback;

/// <summary>
/// What AUTO and ENHANCE choose from: this PC's logical cores, and the graphics adapter LibVLC draws with, read from
/// DXGI (adapter 0, Windows' default for this app). Anything that can't be read reads as absent, never as present.
/// </summary>
public static class HardwareProbe
{
    static readonly Lazy<GpuFacts?> Adapter = new(ReadGpu);

    public static HardwareFacts Detect() => new(Environment.ProcessorCount, GpuSuperResolution: Enhance.SuperResolution(Gpu()));

    public static GpuFacts? Gpu() => Adapter.Value;

    static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    const int SoftwareAdapter = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [DllImport("dxgi.dll")]
    static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    // IDXGIFactory1::EnumAdapters1 is slot 12 (IUnknown 3, IDXGIObject 4, IDXGIFactory 5); IDXGIAdapter::CheckInterfaceSupport
    // is slot 9 and IDXGIAdapter1::GetDesc1 slot 10.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdapters1(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDesc1(IntPtr self, out AdapterDesc1 desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CheckInterfaceSupport(IntPtr self, ref Guid iid, out long umdVersion);

    static T Slot<T>(IntPtr com, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(com), slot * IntPtr.Size));

    static GpuFacts? ReadGpu()
    {
        if (!OperatingSystem.IsWindows()) return null;
        IntPtr factory = IntPtr.Zero, adapter = IntPtr.Zero;
        try
        {
            var iid = IidFactory1;
            if (CreateDXGIFactory1(ref iid, out factory) < 0) return null;
            if (Slot<EnumAdapters1>(factory, 12)(factory, 0, out adapter) < 0) return null;
            if (Slot<GetDesc1>(adapter, 10)(adapter, out var desc) < 0 || (desc.Flags & SoftwareAdapter) != 0) return null;
            var device = IidDxgiDevice;
            Version? driver = null;
            if (Slot<CheckInterfaceSupport>(adapter, 9)(adapter, ref device, out long umd) >= 0)
                driver = new Version((int)(umd >> 48 & 0xFFFF), (int)(umd >> 32 & 0xFFFF), (int)(umd >> 16 & 0xFFFF), (int)(umd & 0xFFFF));
            return new GpuFacts((int)desc.VendorId, desc.Description.Trim(), driver);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or SEHException or COMException)
        {
            return null;
        }
        finally
        {
            if (adapter != IntPtr.Zero) Marshal.Release(adapter);
            if (factory != IntPtr.Zero) Marshal.Release(factory);
        }
    }
}
