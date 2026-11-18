using System.Diagnostics;
using System.Reflection;
using LANVC.Security.Argon2;
using LANVC.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LANVC.Server;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length > 0)
        {
            string command = args[0].ToLowerInvariant();
            switch (command)
            {
                case "--install-service":
                    InstallService();
                    return;

                case "--uninstall-service":
                    UninstallService();
                    return;

                case "--set-password":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Ошибка: укажите пароль: --set-password <новый_пароль>");
                        return;
                    }
                    SetPassword(args[1]);
                    return;

                case "--console":
                    await RunConsoleModeAsync();
                    return;

                case "--help":
                case "-h":
                    PrintHelp();
                    return;
            }
        }

        // Default behavior: if environment is interactive console without args, run console mode; otherwise Windows Service
        if (Environment.UserInteractive)
        {
            await RunConsoleModeAsync();
        }
        else
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddWindowsService(options =>
            {
                options.ServiceName = "LANVC.Server";
            });
            builder.Services.AddHostedService<LanvcWindowsService>();

            var host = builder.Build();
            await host.RunAsync();
        }
    }

    private static async Task RunConsoleModeAsync()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================");
        Console.WriteLine("           LANVC HUB SERVER (Console Mode)        ");
        Console.WriteLine("   Виртуальная локальная сеть для игр и софта     ");
        Console.WriteLine("==================================================");
        Console.ResetColor();

        var config = ServerConfigLoader.LoadOrCreateDefault();
        Console.WriteLine($"Конфигурация: {ServerConfigLoader.ActiveConfigPath}");
        Console.WriteLine($"Виртуальная подсеть: {config.VirtualSubnet} (Сервер: {config.ServerVirtualIp})");
        Console.WriteLine($"Порты: QUIC (UDP) {config.QuicPort}, WebSocket (TCP) {config.WebSocketPort}");
        Console.WriteLine($"Макс. участников: {config.MaxClients}");
        Console.WriteLine("--------------------------------------------------");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var server = new HubServer(config);
        server.OnLog += msg =>
        {
            var color = ConsoleColor.Gray;
            if (msg.Contains("[Security]")) color = ConsoleColor.Yellow;
            else if (msg.Contains("[Transport]")) color = ConsoleColor.Green;
            else if (msg.Contains("[Server]")) color = ConsoleColor.White;

            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            Console.ResetColor();
        };

        await server.StartAsync(cts.Token);

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("Сервер запущен. Нажмите 'q' для остановки или 'p' для смены пароля.");
        Console.ResetColor();

        while (!cts.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                {
                    cts.Cancel();
                    break;
                }
                else if (key.KeyChar == 'p' || key.KeyChar == 'P')
                {
                    Console.Write("\nВведите новый сетевой пароль: ");
                    string? newPwd = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(newPwd))
                    {
                        var (h, s) = Argon2PasswordHasher.HashPassword(newPwd);
                        config.PasswordHash = h;
                        config.PasswordSaltHex = s;
                        ServerConfigLoader.SaveConfig(config);
                        Console.WriteLine("Пароль успешно обновлён в конфигурации!");
                    }
                }
            }
            await Task.Delay(200, cts.Token).ConfigureAwait(false);
        }

        await server.StopAsync();
        Console.WriteLine("Сервер остановлен.");
    }

    private static void SetPassword(string password)
    {
        var config = ServerConfigLoader.LoadOrCreateDefault();
        var (h, s) = Argon2PasswordHasher.HashPassword(password);
        config.PasswordHash = h;
        config.PasswordSaltHex = s;
        ServerConfigLoader.SaveConfig(config);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Успешно] Пароль сети обновлён и захэширован Argon2id.");
        Console.ResetColor();
    }

    private static void InstallService()
    {
        string exePath = Process.GetCurrentProcess().MainModule?.FileName ??
                         Assembly.GetExecutingAssembly().Location;

        Console.WriteLine($"Регистрация Windows Service 'LANVC.Server' ({exePath})...");

        RunProcess("sc.exe", $"create \"LANVC.Server\" binPath= \"\\\"{exePath}\\\"\" start= auto DisplayName= \"LANVC Hub Server Service\"");
        RunProcess("sc.exe", "description \"LANVC.Server\" \"Центральный сервер виртуальной локальной сети LANVC для игр и рабочих станций\"");

        Console.WriteLine("Настройка правил Windows Firewall...");
        RunProcess("netsh.exe", "advfirewall firewall add rule name=\"LANVC Server QUIC\" dir=in action=allow protocol=UDP localport=443");
        RunProcess("netsh.exe", "advfirewall firewall add rule name=\"LANVC Server WSS\" dir=in action=allow protocol=TCP localport=443");

        RunProcess("sc.exe", "start \"LANVC.Server\"");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[Успешно] Служба LANVC.Server зарегистрирована и запущена!");
        Console.ResetColor();
    }

    private static void UninstallService()
    {
        Console.WriteLine("Остановка и удаление Windows Service 'LANVC.Server'...");
        RunProcess("sc.exe", "stop \"LANVC.Server\"");
        RunProcess("sc.exe", "delete \"LANVC.Server\"");

        Console.WriteLine("Удаление правил Windows Firewall...");
        RunProcess("netsh.exe", "advfirewall firewall delete rule name=\"LANVC Server QUIC\"");
        RunProcess("netsh.exe", "advfirewall firewall delete rule name=\"LANVC Server WSS\"");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[Успешно] Служба и правила брандмауэра удалены.");
        Console.ResetColor();
    }

    private static void RunProcess(string file, string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка выполнения {file} {args}: {ex.Message}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Использование LANVC.Server:");
        Console.WriteLine("  LANVC.Server.exe [команда]");
        Console.WriteLine();
        Console.WriteLine("Команды:");
        Console.WriteLine("  --console             Запуск сервера в интерактивном режиме консоли");
        Console.WriteLine("  --install-service     Установка и регистрация службы Windows Service (автозапуск)");
        Console.WriteLine("  --uninstall-service   Остановка и удаление службы Windows Service");
        Console.WriteLine("  --set-password <pwd>  Установить новый сетевой пароль с хешированием Argon2id");
        Console.WriteLine("  --help                Показать эту справку");
    }
}
