using Npgsql;
using System.Data;

namespace MetaSearch.Services;

/// <summary>
/// PostgreSQL 연결 팩토리 (Dapper용).
/// 각 요청마다 새 연결을 열고 using 블록에서 자동 반환한다.
/// </summary>
public class DbService
{
    private readonly string _connectionString;

    public DbService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection 연결 문자열이 없습니다.");
    }

    /// <summary>새 PostgreSQL 연결을 열어 반환한다. 호출자는 반드시 using으로 감싸야 한다.</summary>
    public IDbConnection OpenConnection()
    {
        var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
