using System.Collections.Concurrent;
using System.Net;

namespace LANVC.Security.RateLimiting;

public sealed class TokenBucketRateLimiter
{
    private sealed class ClientRateRecord
    {
        public int FailedAttempts;
        public DateTime LastAttempt;
        public DateTime BannedUntil;
    }

    private readonly ConcurrentDictionary<string, ClientRateRecord> _records = new();
    private readonly int _maxFailedAttempts;
    private readonly TimeSpan _window;

    public TokenBucketRateLimiter(int maxFailedAttempts = 5, int windowMinutes = 1)
    {
        _maxFailedAttempts = maxFailedAttempts;
        _window = TimeSpan.FromMinutes(windowMinutes);
    }

    public bool IsBanned(IPAddress ip, out TimeSpan remainingBan)
    {
        string key = ip.ToString();
        if (_records.TryGetValue(key, out var record))
        {
            var now = DateTime.UtcNow;
            if (record.BannedUntil > now)
            {
                remainingBan = record.BannedUntil - now;
                return true;
            }
        }

        remainingBan = TimeSpan.Zero;
        return false;
    }

    public void RecordFailure(IPAddress ip)
    {
        string key = ip.ToString();
        var record = _records.GetOrAdd(key, _ => new ClientRateRecord());

        lock (record)
        {
            var now = DateTime.UtcNow;
            if (now - record.LastAttempt > _window && record.BannedUntil <= now)
            {
                record.FailedAttempts = 0;
            }

            record.FailedAttempts++;
            record.LastAttempt = now;

            if (record.FailedAttempts >= _maxFailedAttempts)
            {
                // Exponential or tiered ban: 5 min, 30 min, 24 hours
                int multiplier = Math.Min(record.FailedAttempts - _maxFailedAttempts + 1, 3);
                TimeSpan banDuration = multiplier switch
                {
                    1 => TimeSpan.FromMinutes(5),
                    2 => TimeSpan.FromMinutes(30),
                    _ => TimeSpan.FromHours(24)
                };

                record.BannedUntil = now + banDuration;
            }
        }
    }

    public void RecordSuccess(IPAddress ip)
    {
        string key = ip.ToString();
        _records.TryRemove(key, out _);
    }
}
