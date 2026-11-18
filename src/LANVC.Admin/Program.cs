using System.Diagnostics;
using System.Text.Json;
using LANVC.Security.Argon2;
using LANVC.Shared.Models;

namespace LANVC.Admin;

public static class Program
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LANVC",
        "server_config.json");

    private static readonly string LocalConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "server_config.json");

    private static string ActivePath => File.Exists(LocalConfigPath) ? LocalConfigPath : ConfigPath;

    public static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================");
        Console.WriteLine("          LANVC Server Administration Tool        ");
        Console.WriteLine("==================================================");
        Console.ResetColor();

        if (args.Length == 0)
        {
            ShowMenu();
            return;
        }

        string cmd = args[0].ToLowerInvariant();
        switch (cmd)
        {
            case "status":
            case "info":
                ShowStatus();
                break;

            case "set-password":
                if (args.Length < 2)
                {
                    Console.Write("Введите новый пароль сети: ");
                    string? pwd = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(pwd)) SetPassword(pwd);
                }
                else
                {
                    SetPassword(args[1]);
                }
                break;

            case "set-max-clients":
                if (args.Length >= 2 && int.TryParse(args[1], out int max))
                {
                    SetMaxClients(max);
                }
                else
                {
                    Console.WriteLine("Укажите число: set-max-clients <1..250>");
                }
                break;

            case "firewall":
            case "setup-firewall":
                SetupFirewall();
                break;

            case "start":
                RunSc("start LANVC.Server");
                break;

            case "stop":
                RunSc("stop LANVC.Server");
                break;

            case "restart":
                RunSc("stop LANVC.Server");
                Thread.Sleep(1000);
                RunSc("start LANVC.Server");
                break;

            default:
                Console.WriteLine($"Неизвестная команда '{cmd}'. Запустите без аргументов для меню.");
                break;
        }
    }

    private static void ShowMenu()
    {
        while (true)
        {
            ShowStatus();
            Console.WriteLine();
            Console.WriteLine("Доступные действия:");
            Console.WriteLine("  1. Сменить пароль сети (Argon2id)");
            Console.WriteLine("  2. Изменить лимит участников (макс. 10)");
            Console.WriteLine("  3. Настроить правила Windows Firewall");
            Console.WriteLine("  4. Запустить службу LANVC.Server");
            Console.WriteLine("  5. Остановить службу LANVC.Server");
            Console.WriteLine("  6. Перезапустить службу LANVC.Server");
            Console.WriteLine("  0. Выход");
            Console.Write("\nВыберите пункт [0-6]: ");

            string? choice = Console.ReadLine();
            Console.WriteLine();

            switch (choice)
            {
                case "1":
                    Console.Write("Введите новый пароль: ");
                    string? pwd = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(pwd)) SetPassword(pwd);
                    break;
                case "2":
                    Console.Write("Введите максимальное число участников (1-250): ");
                    if (int.TryParse(Console.ReadLine(), out int max)) SetMaxClients(max);
                    break;
                case "3":
                    SetupFirewall();
                    break;
                case "4":
                    RunSc("start LANVC.Server");
                    break;
                case "5":
                    RunSc("stop LANVC.Server");
                    break;
                case "6":
                    RunSc("stop LANVC.Server");
                    Thread.Sleep(1000);
                    RunSc("start LANVC.Server");
                    break;
                case "0":
                case "q":
                    return;
            }
            Console.WriteLine("\nНажмите Enter для продолжения...");
            Console.ReadLine();
            Console.Clear();
        }
    }

    private static void ShowStatus()
    {
        var config = LoadConfig();
        Console.WriteLine($"Файл конфигурации: {ActivePath}");
        Console.WriteLine($"Статус файла: {(File.Exists(ActivePath) ? "Существует" : "Не создан (будет создан по умолчанию)")}");
        Console.WriteLine($"Виртуальная сеть: {config.VirtualSubnet} (IP сервера: {config.ServerVirtualIp})");
        Console.WriteLine($"Порт QUIC (UDP):  {config.QuicPort}");
        Console.WriteLine($"Порт WSS (TCP):   {config.WebSocketPort}");
        Console.WriteLine($"Лимит клиентов:   {config.MaxClients}");
        Console.WriteLine($"Пароль сети:      {(string.IsNullOrEmpty(config.PasswordHash) ? "[Не установлен - вход свободный]" : "[Установлен и защищён Argon2id]")}");

        CheckServiceStatus();
    }

    private static void CheckServiceStatus()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "query LANVC.Server",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            string outText = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(2000);

            if (outText.Contains("RUNNING"))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Служба Windows:   АКТИВНА (RUNNING)");
            }
            else if (outText.Contains("STOPPED"))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Служба Windows:   ОСТАНОВЛЕНА (STOPPED)");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Служба Windows:   НЕ УСТАНОВЛЕНА");
            }
            Console.ResetColor();
        }
        catch
        {
            Console.WriteLine("Служба Windows:   Не удалось проверить статус службы");
        }
    }

    private static void SetPassword(string pwd)
    {
        var config = LoadConfig();
        var (h, s) = Argon2PasswordHasher.HashPassword(pwd);
        config.PasswordHash = h;
        config.PasswordSaltHex = s;
        SaveConfig(config);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Пароль успешно обновлён и сохранён в Argon2id хеше.");
        Console.ResetColor();
    }

    private static void SetMaxClients(int max)
    {
        var config = LoadConfig();
        config.MaxClients = Math.Clamp(max, 1, 250);
        SaveConfig(config);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Максимальное число участников установлено: {config.MaxClients}");
        Console.ResetColor();
    }

    private static void SetupFirewall()
    {
        Console.WriteLine("Настройка правил Windows Defender Firewall...");
        RunProcess("netsh.exe", "advfirewall firewall add rule name=\"LANVC Server QUIC (UDP 443)\" dir=in action=allow protocol=UDP localport=443");
        RunProcess("netsh.exe", "advfirewall firewall add rule name=\"LANVC Server WebSocket (TCP 443)\" dir=in action=allow protocol=TCP localport=443");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Правила входящих соединений для портов 443 UDP и 443 TCP успешно добавлены!");
        Console.ResetColor();
    }

    private static void RunSc(string args)
    {
        Console.WriteLine($"sc.exe {args}");
        RunProcess("sc.exe", args);
    }

    private static void RunProcess(string file, string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            string outText = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(3000);
            if (!string.IsNullOrWhiteSpace(outText))
            {
                Console.WriteLine(outText.Trim());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
        }
    }

    private static ServerConfig LoadConfig()
    {
        if (File.Exists(ActivePath))
        {
            try
            {
                string json = File.ReadAllText(ActivePath);
                var c = JsonSerializer.Deserialize<ServerConfig>(json);
                if (c != null) return c;
            }
            catch { }
        }

        var (h, s) = Argon2PasswordHasher.HashPassword("lanvc2026");
        return new ServerConfig
        {
            ListenAddress = "0.0.0.0",
            QuicPort = 443,
            WebSocketPort = 443,
            VirtualSubnet = "10.77.0.0/24",
            ServerVirtualIp = "10.77.0.1",
            MaxClients = 10,
            PasswordHash = h,
            PasswordSaltHex = s
        };
    }

    private static void SaveConfig(ServerConfig config)
    {
        string? dir = Path.GetDirectoryName(ActivePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ActivePath, json);
    }
}
