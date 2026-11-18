using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LANVC.Client.ViewModels;

public sealed class ParticipantItemViewModel : INotifyPropertyChanged
{
    private string _hostname = string.Empty;
    private string _virtualIp = string.Empty;
    private bool _isSelf;
    private bool _isOnline = true;
    private string _onlineDuration = "00:00:00";
    private int _latencyMs = 12;

    public ushort PeerId { get; set; }

    public string Hostname
    {
        get => _hostname;
        set => SetField(ref _hostname, value);
    }

    public string VirtualIp
    {
        get => _virtualIp;
        set
        {
            if (SetField(ref _virtualIp, value))
            {
                OnPropertyChanged(nameof(DisplayIp));
            }
        }
    }

    public bool IsSelf
    {
        get => _isSelf;
        set
        {
            if (SetField(ref _isSelf, value))
            {
                OnPropertyChanged(nameof(DisplayIp));
            }
        }
    }

    public string DisplayIp => IsSelf ? $"{VirtualIp} · Вы" : VirtualIp;

    public char AvatarInitial => !string.IsNullOrEmpty(Hostname) ? char.ToUpper(Hostname[0]) : '?';

    public bool IsOnline
    {
        get => _isOnline;
        set => SetField(ref _isOnline, value);
    }

    public string OnlineDuration
    {
        get => _onlineDuration;
        set => SetField(ref _onlineDuration, value);
    }

    public int LatencyMs
    {
        get => _latencyMs;
        set => SetField(ref _latencyMs, value);
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
