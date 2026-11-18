using System.Diagnostics;

namespace LANVC.Adapter;

public static class RouteManager
{
    public static bool ConfigureInterface(string interfaceName, string ipAddress, string subnetMask, ushort mtu = 1420)
    {
        try
        {
            // 1. Assign static IP & Subnet
            RunNetsh($"interface ipv4 set address name=\"{interfaceName}\" source=static address={ipAddress} mask={subnetMask}");

            // 2. Set MTU
            RunNetsh($"interface ipv4 set subinterface \"{interfaceName}\" mtu={mtu} store=active");

            // 3. Lower interface metric so Windows prioritizes it for LAN traffic
            RunNetsh($"interface ipv4 set interface \"{interfaceName}\" metric=15");

            // 4. Add route for broadcast (255.255.255.255/32)
            RunNetsh($"interface ipv4 add route 255.255.255.255/32 \"{interfaceName}\" {ipAddress} metric=5 store=active");

            // 5. Add route for multicast (224.0.0.0/4)
            RunNetsh($"interface ipv4 add route 224.0.0.0/4 \"{interfaceName}\" {ipAddress} metric=10 store=active");

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void CleanupRoutes(string interfaceName)
    {
        try
        {
            RunNetsh($"interface ipv4 delete route 255.255.255.255/32 \"{interfaceName}\"");
            RunNetsh($"interface ipv4 delete route 224.0.0.0/4 \"{interfaceName}\"");
        }
        catch
        {
            // Suppress errors during cleanup
        }
    }

    private static string RunNetsh(string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(3000);
        return output;
    }
}
