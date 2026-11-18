using System.Text.Json;
using LANVC.Security.Argon2;
using LANVC.Shared.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LANVC.Server;

public sealed class LanvcWindowsService : BackgroundService
{
    private readonly ILogger<LanvcWindowsService> _logger;
    private HubServer? _server;

    public LanvcWindowsService(ILogger<LanvcWindowsService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LANVC Windows Service starting...");

        var config = ServerConfigLoader.LoadOrCreateDefault();
        _server = new HubServer(config);
        _server.OnLog += msg => _logger.LogInformation("{ServerLog}", msg);

        await _server.StartAsync(stoppingToken).ConfigureAwait(false);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        finally
        {
            if (_server != null)
            {
                await _server.StopAsync().ConfigureAwait(false);
            }
            _logger.LogInformation("LANVC Windows Service stopped.");
        }
    }
}

public static class ServerConfigLoader
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LANVC",
        "server_config.json");

    private static readonly string LocalConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "server_config.json");

    public static string ActiveConfigPath => File.Exists(LocalConfigPath) ? LocalConfigPath : ConfigPath;

    public static ServerConfig LoadOrCreateDefault()
    {
        string targetPath = File.Exists(LocalConfigPath) ? LocalConfigPath : ConfigPath;

        if (File.Exists(targetPath))
        {
            try
            {
                string json = File.ReadAllText(targetPath);
                var cfg = JsonSerializer.Deserialize<ServerConfig>(json);
                if (cfg != null) return cfg;
            }
            catch { }
        }

        // Create default config
        var (hash, salt) = Argon2PasswordHasher.HashPassword("lanvc2026");

        var defaultConfig = new ServerConfig
        {
            ListenAddress = "0.0.0.0",
            QuicPort = 443,
            WebSocketPort = 443,
            VirtualSubnet = "10.77.0.0/24",
            ServerVirtualIp = "10.77.0.1",
            MaxClients = 10,
            PasswordHash = hash,
            PasswordSaltHex = salt,
            EnableBroadcastRelay = true
        };

        SaveConfig(defaultConfig, targetPath);
        return defaultConfig;
    }

    public static void SaveConfig(ServerConfig config, string? path = null)
    {
        string targetPath = path ?? ActiveConfigPath;
        string? dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(targetPath, json);
    }
}
