using System.Runtime.InteropServices;

namespace LANVC.Adapter.Native;

public static class WintunNative
{
    private const string DllName = "wintun.dll";

    [DllImport(DllName, EntryPoint = "WintunCreateAdapter", CallingConvention = CallingConvention.Winapi, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr WintunCreateAdapter(
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        [MarshalAs(UnmanagedType.LPWStr)] string tunnelType,
        ref Guid requestedGuid);

    [DllImport(DllName, EntryPoint = "WintunOpenAdapter", CallingConvention = CallingConvention.Winapi, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr WintunOpenAdapter([MarshalAs(UnmanagedType.LPWStr)] string name);

    [DllImport(DllName, EntryPoint = "WintunCloseAdapter", CallingConvention = CallingConvention.Winapi)]
    public static extern void WintunCloseAdapter(IntPtr adapter);

    [DllImport(DllName, EntryPoint = "WintunDeleteDriver", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern bool WintunDeleteDriver();

    [DllImport(DllName, EntryPoint = "WintunGetAdapterLUID", CallingConvention = CallingConvention.Winapi)]
    public static extern void WintunGetAdapterLUID(IntPtr adapter, out ulong luid);

    [DllImport(DllName, EntryPoint = "WintunGetRunningDriverVersion", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern uint WintunGetRunningDriverVersion();

    [DllImport(DllName, EntryPoint = "WintunStartSession", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern IntPtr WintunStartSession(IntPtr adapter, uint capacity);

    [DllImport(DllName, EntryPoint = "WintunEndSession", CallingConvention = CallingConvention.Winapi)]
    public static extern void WintunEndSession(IntPtr session);

    [DllImport(DllName, EntryPoint = "WintunGetReadWaitEvent", CallingConvention = CallingConvention.Winapi)]
    public static extern IntPtr WintunGetReadWaitEvent(IntPtr session);

    [DllImport(DllName, EntryPoint = "WintunReceivePacket", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern IntPtr WintunReceivePacket(IntPtr session, out uint packetSize);

    [DllImport(DllName, EntryPoint = "WintunReleaseReceivePacket", CallingConvention = CallingConvention.Winapi)]
    public static extern void WintunReleaseReceivePacket(IntPtr session, IntPtr packet);

    [DllImport(DllName, EntryPoint = "WintunAllocateSendPacket", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
    public static extern IntPtr WintunAllocateSendPacket(IntPtr session, uint packetSize);

    [DllImport(DllName, EntryPoint = "WintunSendPacket", CallingConvention = CallingConvention.Winapi)]
    public static extern void WintunSendPacket(IntPtr session, IntPtr packet);
}
