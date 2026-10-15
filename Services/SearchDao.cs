using Dapper;

namespace MetaSearch.Services;

/// <summary>
/// 검색 기록 및 결과 캐싱 DAO.
/// PostgreSQL + Dapper.
/// TOP → LIMIT, GETDATE() → NOW(), DATEADD → INTERVAL 문법으로 변환.
/// </summary>
public class SearchDao
{
    private readonly DbService _db;

    public SearchDao(DbService db) => _db = db;

    /// <summary>검색 기록 저장</summary>
    public int AddSearchHistory(string userID, string query, string keyword)
    {
        try
        {
            using var conn = _db.OpenConnection();
            conn.Execute(
                "INSERT INTO searchhistory (userid, query, keyword, searchtime) VALUES (@userID, @query, @keyword, NOW())",
                new { userID, query, keyword });
            return 1;
        }
        catch { return -1; }
    }

    /// <summary>최근 검색어 조회 (중복 제거, 최신순, id 포함)</summary>
    public IEnumerable<dynamic> GetRecentSearches(string userID, int top = 10)
    {
        using var conn = _db.OpenConnection();
        return conn.Query(
            @"SELECT MAX(no) AS id, query, MAX(searchtime) AS searchtime
              FROM searchhistory WHERE userid = @userID
              GROUP BY query ORDER BY MAX(searchtime) DESC
              LIMIT @top",
            new { userID, top });
    }

    /// <summary>특정 검색어 기록 삭제</summary>
    public int DeleteSearchHistory(string userID, string query)
    {
        try
        {
            using var conn = _db.OpenConnection();
            conn.Execute(
                "DELETE FROM searchhistory WHERE userid = @userID AND query = @query",
                new { userID, query });
            return 1;
        }
        catch { return -1; }
    }

    /// <summary>전체 인기 검색어 TOP N</summary>
    public IEnumerable<dynamic> GetTopSearches(int top = 10)
    {
        using var conn = _db.OpenConnection();
        return conn.Query(
            @"SELECT query, COUNT(*) AS searchcount
              FROM searchhistory GROUP BY query
              ORDER BY searchcount DESC LIMIT @top",
            new { top });
    }

    /// <summary>개인 인기 검색어 TOP N</summary>
    public IEnumerable<dynamic> GetUserTopSearches(string userID, int top = 5)
    {
        using var conn = _db.OpenConnection();
        return conn.Query(
            @"SELECT query, COUNT(*) AS searchcount
              FROM searchhistory WHERE userid = @userID
              GROUP BY query ORDER BY searchcount DESC LIMIT @top",
            new { userID, top });
    }

    /// <summary>총 검색 횟수</summary>
    public int GetTotalSearchCount(string userID)
    {
        try
        {
            using var conn = _db.OpenConnection();
            // PostgreSQL COUNT(*)는 bigint — long으로 받아 int 변환
            return Convert.ToInt32(conn.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM searchhistory WHERE userid = @userID",
                new { userID }));
        }
        catch { return 0; }
    }

    /// <summary>가장 많이 검색한 검색어 1개</summary>
    public string GetTopKeyword(string userID)
    {
        try
        {
            using var conn = _db.OpenConnection();
            return conn.QueryFirstOrDefault<string>(
                @"SELECT query FROM searchhistory WHERE userid = @userID
                  GROUP BY query ORDER BY COUNT(*) DESC LIMIT 1",
                new { userID }) ?? "-";
        }
        catch { return "-"; }
    }

    /// <summary>가장 많이 분류된 AI 키워드 1개</summary>
    public string GetTopAIKeyword(string userID)
    {
        try
        {
            using var conn = _db.OpenConnection();
            return conn.QueryFirstOrDefault<string>(
                @"SELECT keyword FROM searchhistory
                  WHERE userid = @userID AND keyword IS NOT NULL AND keyword <> ''
                  GROUP BY keyword ORDER BY COUNT(*) DESC LIMIT 1",
                new { userID }) ?? "-";
        }
        catch { return "-"; }
    }

    /// <summary>캐시된 검색 결과 조회 (7일 이내)</summary>
    public IEnumerable<dynamic> GetCachedResults(string query)
    {
        using var conn = _db.OpenConnection();
        return conn.Query(
            @"SELECT title, url, description, source FROM searchresultcache
              WHERE query = @query AND cachedat > NOW() - INTERVAL '7 days'
              ORDER BY cacheid",
            new { query });
    }

    /// <summary>검색 결과 캐시 저장 (기존 캐시 삭제 후 새로 저장)</summary>
    public void SaveResultCache(string query, List<Models.SearchResult> results)
    {
        try
        {
            using var conn = _db.OpenConnection();
            conn.Execute("DELETE FROM searchresultcache WHERE query = @query", new { query });
            foreach (var r in results)
            {
                conn.Execute(
                    @"INSERT INTO searchresultcache (query, title, url, description, source, cachedat)
                      VALUES (@query, @title, @url, @desc, @src, NOW())",
                    new { query, title = r.Title, url = r.Url, desc = r.Description, src = r.Source });
            }
        }
        catch { }
    }

    /// <summary>기간별 검색 기록 삭제 (MSSQL DATEADD → PostgreSQL INTERVAL)</summary>
    public void DeleteSearchHistoryByPeriod(string userID, string period)
    {
        using var conn = _db.OpenConnection();
        if (period == "all" || string.IsNullOrEmpty(period))
        {
            conn.Execute("DELETE FROM searchhistory WHERE userid = @userID", new { userID });
            return;
        }
        string sql = period switch
        {
            "1h"  => "DELETE FROM searchhistory WHERE userid = @userID AND searchtime > NOW() - INTERVAL '1 hour'",
            "12h" => "DELETE FROM searchhistory WHERE userid = @userID AND searchtime > NOW() - INTERVAL '12 hours'",
            "1d"  => "DELETE FROM searchhistory WHERE userid = @userID AND searchtime > NOW() - INTERVAL '1 day'",
            "7d"  => "DELETE FROM searchhistory WHERE userid = @userID AND searchtime > NOW() - INTERVAL '7 days'",
            "30d" => "DELETE FROM searchhistory WHERE userid = @userID AND searchtime > NOW() - INTERVAL '30 days'",
            _     => "DELETE FROM searchhistory WHERE userid = @userID"
        };
        conn.Execute(sql, new { userID });
    }

    /// <summary>자동완성 제안어 (검색어 접두사 매칭)</summary>
    public IEnumerable<string> GetSuggestions(string prefix)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<string>(
            @"SELECT query FROM searchhistory
              WHERE query LIKE @prefix
              GROUP BY query ORDER BY COUNT(*) DESC LIMIT 5",
            new { prefix = prefix + "%" });
    }

    /// <summary>7일 이상 된 검색 기록·캐시 자동 삭제</summary>
    public void CleanupOldRecords()
    {
        try
        {
            using var conn = _db.OpenConnection();
            conn.Execute("DELETE FROM searchhistory WHERE searchtime < NOW() - INTERVAL '7 days'");
            conn.Execute("DELETE FROM searchresultcache WHERE cachedat < NOW() - INTERVAL '7 days'");
        }
        catch { }
    }
}
