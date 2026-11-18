using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using LANVC.Client.Services;
using LANVC.Shared.Models;
using LANVC.Shared.Protocol;
using LANVC.Transport.Abstractions;

namespace LANVC.Client.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ClientEngine _engine;
    private readonly ProfileManager _profileManager;
    private readonly DispatcherTimer _uiTimer;

    private string _currentTab = "Home";
    private string _serverAddress = "203.0.113.42";
    private int _serverPort = 443;
    private string _password = string.Empty;
    private bool _isPasswordVisible;
    private string _statusMessage = string.Empty;

    private bool _isConnected;
    private bool _isConnecting;
    private string _virtualIp = "0.0.0.0";
    private string _transportName = "QUIC (UDP)";
    private string _transportBadge = "Готов";
    private string _downloadSpeed = "↓ 0.0 КБ/с";
    private string _uploadSpeed = "↑ 0.0 КБ/с";
    private string _packetsStats = "Пакетов: 0 / 0";
    private string _latencyStats = "Задержка: -- мс";
    private string _uptimeStats = "00:00:00";
    private string _participantsHeader = "Участники (0 / 10)";

    public ObservableCollection<ParticipantItemViewModel> Participants { get; } = new();
    public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
    public ObservableCollection<string> DiagnosticsLogs { get; } = new();

    public ClientConfig Settings => _profileManager.Config;

    public string CurrentTab
    {
        get => _currentTab;
        set => SetField(ref _currentTab, value);
    }

    public string ServerAddress
    {
        get => _serverAddress;
        set => SetField(ref _serverAddress, value);
    }

    public int ServerPort
    {
        get => _serverPort;
        set => SetField(ref _serverPort, value);
    }

    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        set => SetField(ref _isPasswordVisible, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetField(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(IsNotConnected));
                OnPropertyChanged(nameof(ConnectionStateText));
            }
        }
    }

    public bool IsNotConnected => !IsConnected && !IsConnecting;

    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            if (SetField(ref _isConnecting, value))
            {
                OnPropertyChanged(nameof(IsNotConnected));
            }
        }
    }

    public string ConnectionStateText => IsConnected ? "Подключено" : "Отключено";

    public string VirtualIp
    {
        get => _virtualIp;
        set => SetField(ref _virtualIp, value);
    }

    public string TransportName
    {
        get => _transportName;
        set => SetField(ref _transportName, value);
    }

    public string TransportBadge
    {
        get => _transportBadge;
        set => SetField(ref _transportBadge, value);
    }

    public string DownloadSpeed
    {
        get => _downloadSpeed;
        set => SetField(ref _downloadSpeed, value);
    }

    public string UploadSpeed
    {
        get => _uploadSpeed;
        set => SetField(ref _uploadSpeed, value);
    }

    public string PacketsStats
    {
        get => _packetsStats;
        set => SetField(ref _packetsStats, value);
    }

    public string LatencyStats
    {
        get => _latencyStats;
        set => SetField(ref _latencyStats, value);
    }

    public string UptimeStats
    {
        get => _uptimeStats;
        set => SetField(ref _uptimeStats, value);
    }

    public string ParticipantsHeader
    {
        get => _participantsHeader;
        set => SetField(ref _participantsHeader, value);
    }

    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand TogglePasswordVisibilityCommand { get; }
    public RelayCommand CopyIpCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand OpenDiagnosticsCommand { get; }
    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand ExportDiagnosticsCommand { get; }
    public RelayCommand ApplyProfileCommand { get; }

    public MainViewModel()
    {
        _engine = new ClientEngine();
        _profileManager = new ProfileManager();

        // Load active profile
        var activeProfile = _profileManager.Config.Profiles.FirstOrDefault();
        if (activeProfile != null)
        {
            ServerAddress = activeProfile.ServerAddress;
            ServerPort = activeProfile.Port;
            string? savedPwd = ProfileManager.LoadPassword(activeProfile.ServerAddress);
            if (!string.IsNullOrEmpty(savedPwd))
            {
                Password = savedPwd;
            }
        }

        foreach (var p in _profileManager.Config.Profiles)
        {
            Profiles.Add(p);
        }

        // Setup commands
        ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => IsNotConnected);
        DisconnectCommand = new RelayCommand(async () => await DisconnectAsync(), () => IsConnected);
        TogglePasswordVisibilityCommand = new RelayCommand(() => IsPasswordVisible = !IsPasswordVisible);
        CopyIpCommand = new RelayCommand(CopyIpToClipboard);
        NavigateCommand = new RelayCommand(param => CurrentTab = param?.ToString() ?? "Home");
        OpenDiagnosticsCommand = new RelayCommand(() => CurrentTab = "About");
        SaveProfileCommand = new RelayCommand(SaveCurrentProfile);
        DeleteProfileCommand = new RelayCommand(param => DeleteProfile(param as ConnectionProfile));
        ApplyProfileCommand = new RelayCommand(param => ApplyProfile(param as ConnectionProfile));
        ExportDiagnosticsCommand = new RelayCommand(ExportDiagnostics);

        // Event subscriptions
        _engine.OnLog += msg =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                DiagnosticsLogs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
                if (DiagnosticsLogs.Count > 500) DiagnosticsLogs.RemoveAt(0);
            });
        };

        _engine.OnConnectionStateChanged += connected =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                IsConnected = connected;
                IsConnecting = false;
                if (connected)
                {
                    VirtualIp = _engine.AssignedVirtualIpString;
                    TransportBadge = "Активен";
                    UpdateSelfParticipant();
                }
                else
                {
                    VirtualIp = "0.0.0.0";
                    TransportBadge = "Отключен";
                    Participants.Clear();
                    ParticipantsHeader = "Участники (0 / 10)";
                }
            });
        };

        _engine.OnTransportChanged += transport =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                TransportName = transport == TransportType.Quic ? "QUIC (UDP)" : "WebSocket (WSS)";
            });
        };

        _engine.OnPeerListUpdated += peers =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() => UpdateParticipants(peers));
        };

        // UI refresh timer (1 second interval)
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();
    }

    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerAddress))
        {
            StatusMessage = "Введите IP-адрес сервера";
            return;
        }

        IsConnecting = true;
        StatusMessage = "Подключение к серверу...";

        // Save password securely in Windows Credential Manager
        if (!string.IsNullOrEmpty(Password))
        {
            ProfileManager.SavePassword(ServerAddress, Environment.MachineName, Password);
        }

        try
        {
            await _engine.ConnectAsync(
                ServerAddress,
                ServerPort,
                Password,
                Environment.MachineName,
                _profileManager.Config.DefaultTransport,
                mtu: _profileManager.Config.VirtualMtu);

            StatusMessage = "Подключено к виртуальной сети";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            IsConnecting = false;
        }
    }

    private async Task DisconnectAsync()
    {
        StatusMessage = "Отключение...";
        await _engine.DisconnectAsync();
        StatusMessage = "Отключено";
    }

    private void CopyIpToClipboard()
    {
        if (VirtualIp != "0.0.0.0")
        {
            Clipboard.SetText(VirtualIp);
            StatusMessage = "IP-адрес скопирован в буфер обмена";
        }
    }

    private void SaveCurrentProfile()
    {
        var profile = Profiles.FirstOrDefault(p => p.ServerAddress == ServerAddress) ?? new ConnectionProfile();
        profile.Name = $"Сеть {ServerAddress}";
        profile.ServerAddress = ServerAddress;
        profile.Port = ServerPort;
        profile.Username = Environment.MachineName;

        if (!Profiles.Contains(profile))
        {
            Profiles.Add(profile);
            _profileManager.Config.Profiles.Add(profile);
        }
        _profileManager.SaveConfig();
        StatusMessage = "Профиль сохранён";
    }

    private void DeleteProfile(ConnectionProfile? profile)
    {
        if (profile != null)
        {
            Profiles.Remove(profile);
            _profileManager.Config.Profiles.Remove(profile);
            _profileManager.SaveConfig();
        }
    }

    private void ApplyProfile(ConnectionProfile? profile)
    {
        if (profile != null)
        {
            ServerAddress = profile.ServerAddress;
            ServerPort = profile.Port;
            string? pwd = ProfileManager.LoadPassword(profile.ServerAddress);
            if (!string.IsNullOrEmpty(pwd)) Password = pwd;
            CurrentTab = "Home";
            StatusMessage = $"Выбран профиль: {profile.Name}";
        }
    }

    private void ExportDiagnostics()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string file = System.IO.Path.Combine(desktop, $"LANVC_Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            using var sw = new System.IO.StreamWriter(file);
            sw.WriteLine("=== LANVC DIAGNOSTICS REPORT ===");
            sw.WriteLine($"Generated at: {DateTime.Now}");
            sw.WriteLine($"Status: {ConnectionStateText}");
            sw.WriteLine($"Server: {ServerAddress}:{ServerPort}");
            sw.WriteLine($"Virtual IP: {VirtualIp}");
            sw.WriteLine($"Transport: {TransportName} ({TransportBadge})");
            sw.WriteLine($"Latency: {LatencyStats}");
            sw.WriteLine($"Traffic: {DownloadSpeed} / {UploadSpeed}");
            sw.WriteLine($"Packets: {PacketsStats}");
            sw.WriteLine($"Active Peers ({Participants.Count}):");
            foreach (var p in Participants)
            {
                sw.WriteLine($"  - {p.Hostname} ({p.VirtualIp}) Online: {p.OnlineDuration} Ping: {p.LatencyMs}ms");
            }
            sw.WriteLine("\n--- LOGS ---");
            foreach (var log in DiagnosticsLogs)
            {
                sw.WriteLine(log);
            }
            StatusMessage = $"Отчёт сохранён на Рабочий стол: {System.IO.Path.GetFileName(file)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка экспорта: {ex.Message}";
        }
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        if (IsConnected)
        {
            var stats = _engine.Statistics;
            DownloadSpeed = stats.FormatDownloadSpeed();
            UploadSpeed = stats.FormatUploadSpeed();
            PacketsStats = $"Пакетов: {stats.TotalPacketsReceived:N0} / {stats.TotalPacketsSent:N0}";
            LatencyStats = $"Задержка: {stats.LatencyMs} мс";

            if (_engine.ConnectedAt.HasValue)
            {
                var ts = DateTime.UtcNow - _engine.ConnectedAt.Value;
                UptimeStats = $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
        }
    }

    private void UpdateParticipants(List<PeerDto> peers)
    {
        Participants.Clear();
        foreach (var p in peers)
        {
            Participants.Add(new ParticipantItemViewModel
            {
                PeerId = p.PeerId,
                Hostname = p.Hostname,
                VirtualIp = p.VirtualIpString,
                IsSelf = p.PeerId == _engine.PeerId,
                IsOnline = p.IsOnline,
                OnlineDuration = p.FormattedOnlineDuration,
                LatencyMs = p.LatencyMs
            });
        }
        ParticipantsHeader = $"Участники ({Participants.Count} / 10)";
    }

    private void UpdateSelfParticipant()
    {
        if (!Participants.Any(p => p.IsSelf))
        {
            Participants.Insert(0, new ParticipantItemViewModel
            {
                PeerId = _engine.PeerId,
                Hostname = Environment.MachineName,
                VirtualIp = _engine.AssignedVirtualIpString,
                IsSelf = true,
                IsOnline = true,
                OnlineDuration = "00:00:01",
                LatencyMs = 1
            });
        }
        ParticipantsHeader = $"Участники ({Participants.Count} / 10)";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
