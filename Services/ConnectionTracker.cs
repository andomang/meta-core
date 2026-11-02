using System.Collections.Concurrent;

namespace MetaSearch.Services;

/// <summary>실시간 접속 IP 추적 — 최근 5분 이내 요청 IP를 메모리에 보관.</summary>
public class ConnectionTracker
{
    // IP → 마지막 요청 시각
    private readonly ConcurrentDictionary<string, DateTime> _connections = new();

    /// <summary>요청 IP 기록 (미들웨어에서 호출)</summary>
    public void Track(string ip)
    {
        _connections[ip] = DateTime.UtcNow;
    }

    /// <summary>최근 5분 이내 접속 IP 목록 반환 (오래된 항목 자동 정리)</summary>
    public List<(string Ip, DateTime LastSeen)> GetActiveConnections()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var expired = new List<string>();
        var active = new List<(string, DateTime)>();

        foreach (var kv in _connections)
        {
            if (kv.Value < cutoff)
                expired.Add(kv.Key);
            else
                active.Add((kv.Key, kv.Value));
        }

        // 만료 항목 정리
        foreach (var key in expired)
            _connections.TryRemove(key, out _);

        return active.OrderByDescending(x => x.Item2).ToList();
    }
}
