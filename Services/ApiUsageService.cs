using Dapper;

namespace MetaSearch.Services;

/// <summary>
/// API 호출 수 카운팅 서비스.
/// 엔진별 일일 호출 횟수를 DB에 기록하여 무료 티어 한도 관리.
/// Serper(Google): 2,500 크레딧 1회성 / Brave: 2,000콜/월
/// </summary>
public class ApiUsageService
{
    private readonly DbService _db;

    public ApiUsageService(DbService db) => _db = db;

    /// <summary>해당 엔진의 오늘 호출 수를 1 증가</summary>
    public void Increment(string engine)
    {
        try
        {
            using var conn = _db.OpenConnection();
            conn.Execute(
                @"INSERT INTO api_usage (engine, call_date, call_count)
                  VALUES (@engine, CURRENT_DATE, 1)
                  ON CONFLICT (engine, call_date)
                  DO UPDATE SET call_count = api_usage.call_count + 1",
                new { engine });
        }
        catch { }
    }

    /// <summary>오늘 호출 수 조회</summary>
    public int GetTodayCount(string engine)
    {
        try
        {
            using var conn = _db.OpenConnection();
            return conn.QueryFirstOrDefault<int>(
                "SELECT call_count FROM api_usage WHERE engine=@engine AND call_date=CURRENT_DATE",
                new { engine });
        }
        catch { return 0; }
    }

    /// <summary>이번 달 총 호출 수 조회</summary>
    public int GetMonthlyCount(string engine)
    {
        try
        {
            using var conn = _db.OpenConnection();
            return conn.QueryFirstOrDefault<int>(
                @"SELECT COALESCE(SUM(call_count), 0) FROM api_usage
                  WHERE engine=@engine
                  AND call_date >= DATE_TRUNC('month', CURRENT_DATE)",
                new { engine });
        }
        catch { return 0; }
    }

    /// <summary>전체 누적 호출 수 조회 (Serper 같은 1회성 크레딧용)</summary>
    public int GetTotalCount(string engine)
    {
        try
        {
            using var conn = _db.OpenConnection();
            return conn.QueryFirstOrDefault<int>(
                "SELECT COALESCE(SUM(call_count), 0) FROM api_usage WHERE engine=@engine",
                new { engine });
        }
        catch { return 0; }
    }

    /// <summary>관리자용 전체 엔진 사용량 조회</summary>
    public IEnumerable<dynamic> GetAllUsageSummary()
    {
        try
        {
            using var conn = _db.OpenConnection();
            return conn.Query(
                @"SELECT engine,
                         COALESCE(SUM(call_count), 0) AS total_count,
                         COALESCE(SUM(CASE WHEN call_date >= DATE_TRUNC('month', CURRENT_DATE) THEN call_count ELSE 0 END), 0) AS monthly_count,
                         COALESCE(SUM(CASE WHEN call_date = CURRENT_DATE THEN call_count ELSE 0 END), 0) AS today_count
                  FROM api_usage
                  GROUP BY engine
                  ORDER BY engine");
        }
        catch { return Enumerable.Empty<dynamic>(); }
    }
}
