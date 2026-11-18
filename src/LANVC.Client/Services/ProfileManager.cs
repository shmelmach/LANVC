using System.IO;
using System.Text.Json;
using LANVC.Security.Credentials;
using LANVC.Shared.Models;

namespace LANVC.Client.Services;

public sealed class ProfileManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LANVC");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "client_config.json");

    public ClientConfig Config { get; private set; }

    public ProfileManager()
    {
        Config = LoadConfig();
    }

    public ClientConfig LoadConfig()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                string json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<ClientConfig>(json);
                if (loaded != null)
                {
                    if (loaded.Profiles.Count == 0)
                    {
                        loaded.Profiles.Add(CreateDefaultProfile());
                    }
                    return loaded;
                }
            }
            catch { }
        }

        var cfg = new ClientConfig();
        cfg.Profiles.Add(CreateDefaultProfile());
        SaveConfig(cfg);
        return cfg;
    }

    public void SaveConfig(ClientConfig? config = null)
    {
        if (config != null) Config = config;

        try
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }

            string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }

    public static void SavePassword(string serverAddress, string username, string password)
    {
        WindowsCredentialManager.SavePassword(serverAddress, username, password);
    }

    public static string? LoadPassword(string serverAddress)
    {
        return WindowsCredentialManager.LoadPassword(serverAddress);
    }

    private static ConnectionProfile CreateDefaultProfile()
    {
        return new ConnectionProfile
        {
            Name = "Основной сервер",
            ServerAddress = "203.0.113.42",
            Port = 443,
            Transport = TransportPreference.Auto,
            Username = Environment.MachineName
        };
    }
}
