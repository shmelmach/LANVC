using System.Runtime.InteropServices;
using LANVC.Adapter.Native;
using Microsoft.Win32.SafeHandles;

namespace LANVC.Adapter;

public sealed class WintunAdapter : IDisposable
{
    private const uint RingCapacity = 0x400000; // 4 MiB
    private readonly string _adapterName;
    private readonly string _tunnelType;

    private IntPtr _adapterHandle = IntPtr.Zero;
    private IntPtr _sessionHandle = IntPtr.Zero;
    private Thread? _receiveThread;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public string AdapterName => _adapterName;
    public bool IsRunning => _sessionHandle != IntPtr.Zero && !_disposed;

    public event Action<byte[]>? OnPacketReceived;
    public event Action<string>? OnLog;

    public WintunAdapter(string adapterName = "LANVC", string tunnelType = "LANVC_Tunnel")
    {
        _adapterName = adapterName;
        _tunnelType = tunnelType;
    }

    public bool Start(string virtualIp, string subnetMask, ushort mtu = 1420)
    {
        if (_sessionHandle != IntPtr.Zero)
            return true;

        try
        {
            OnLog?.Invoke($"[Wintun] Initializing adapter '{_adapterName}'...");
            Guid guid = Guid.NewGuid();

            _adapterHandle = WintunNative.WintunCreateAdapter(_adapterName, _tunnelType, ref guid);
            if (_adapterHandle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                // If it already exists, try opening it
                _adapterHandle = WintunNative.WintunOpenAdapter(_adapterName);
                if (_adapterHandle == IntPtr.Zero)
                {
                    OnLog?.Invoke($"[Wintun] Failed to create or open adapter. Error code: {err}");
                    return false;
                }
            }

            _sessionHandle = WintunNative.WintunStartSession(_adapterHandle, RingCapacity);
            if (_sessionHandle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                OnLog?.Invoke($"[Wintun] Failed to start Wintun session. Error code: {err}");
                CloseHandles();
                return false;
            }

            // Configure IP and routes
            OnLog?.Invoke($"[Wintun] Configuring IP {virtualIp}/{subnetMask} (MTU {mtu})...");
            bool configSuccess = RouteManager.ConfigureInterface(_adapterName, virtualIp, subnetMask, mtu);
            if (!configSuccess)
            {
                OnLog?.Invoke("[Wintun] Warning: RouteManager encountered an issue configuring IP/routes");
            }

            _cts = new CancellationTokenSource();
            _receiveThread = new Thread(ReceiveLoop)
            {
                Name = "WintunReceiveLoop",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _receiveThread.Start();

            OnLog?.Invoke("[Wintun] Adapter online and listening for packets");
            return true;
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"[Wintun] Initialization exception: {ex.Message}");
            CloseHandles();
            return false;
        }
    }

    public bool SendPacket(ReadOnlySpan<byte> packet)
    {
        if (_sessionHandle == IntPtr.Zero || _disposed || packet.IsEmpty)
            return false;

        try
        {
            IntPtr buffer = WintunNative.WintunAllocateSendPacket(_sessionHandle, (uint)packet.Length);
            if (buffer == IntPtr.Zero)
                return false;

            unsafe
            {
                fixed (byte* src = packet)
                {
                    Buffer.MemoryCopy(src, (void*)buffer, packet.Length, packet.Length);
                }
            }

            WintunNative.WintunSendPacket(_sessionHandle, buffer);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ReceiveLoop()
    {
        IntPtr waitEventHandle = WintunNative.WintunGetReadWaitEvent(_sessionHandle);
        using var waitHandle = new AutoResetEvent(false)
        {
            SafeWaitHandle = new SafeWaitHandle(waitEventHandle, ownsHandle: false)
        };

        var cancelToken = _cts?.Token ?? CancellationToken.None;

        while (!cancelToken.IsCancellationRequested && !_disposed && _sessionHandle != IntPtr.Zero)
        {
            while (true)
            {
                IntPtr packetPtr = WintunNative.WintunReceivePacket(_sessionHandle, out uint packetSize);
                if (packetPtr == IntPtr.Zero)
                    break; // No more packets in ring buffer right now

                try
                {
                    if (packetSize > 0)
                    {
                        byte[] packetData = new byte[packetSize];
                        Marshal.Copy(packetPtr, packetData, 0, (int)packetSize);
                        OnPacketReceived?.Invoke(packetData);
                    }
                }
                finally
                {
                    WintunNative.WintunReleaseReceivePacket(_sessionHandle, packetPtr);
                }
            }

            // Wait for incoming packet event or cancellation timeout
            waitHandle.WaitOne(50);
        }
    }

    private void CloseHandles()
    {
        if (_sessionHandle != IntPtr.Zero)
        {
            WintunNative.WintunEndSession(_sessionHandle);
            _sessionHandle = IntPtr.Zero;
        }

        if (_adapterHandle != IntPtr.Zero)
        {
            WintunNative.WintunCloseAdapter(_adapterHandle);
            _adapterHandle = IntPtr.Zero;
        }
    }

    public void Stop()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();

        try
        {
            RouteManager.CleanupRoutes(_adapterName);
        }
        catch { }

        CloseHandles();
        OnLog?.Invoke("[Wintun] Adapter stopped and resources released");
    }
}
