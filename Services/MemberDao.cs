using System.Security.Cryptography;
using System.Text;
using Dapper;
using MetaSearch.Models;

namespace MetaSearch.Services;

/// <summary>
/// 회원(Member) 관련 DB 작업 DAO.
/// PostgreSQL 문법: IIF/ISNULL → CASE/COALESCE, TOP → LIMIT, RTRIM → TRIM.
/// </summary>
public class MemberDao
{
    private readonly DbService _db;

    public MemberDao(DbService db) => _db = db;

    /// <summary>아이디·비밀번호 검증 (MD5 해시 비교, 활성 계정만 허용)</summary>
    public bool Authenticate(string id, string pw)
    {
        string hash = GetMD5(pw);
        using var conn = _db.OpenConnection();
        // PostgreSQL VARCHAR는 공백 패딩 없음 — TRIM 불필요하지만 호환성 유지
        var uid = conn.QueryFirstOrDefault<string>(
            "SELECT userid FROM members WHERE userid = @id AND passwd = @hash AND (TRIM(status) = 'true' OR TRIM(status) = '1')",
            new { id, hash });
        return uid != null;
    }

    /// <summary>아이디 중복 확인 (사용 가능하면 true)</summary>
    public bool VerifyUserID(string id)
    {
        using var conn = _db.OpenConnection();
        var uid = conn.QueryFirstOrDefault<string>(
            "SELECT userid FROM members WHERE userid = @id", new { id });
        return uid == null;
    }

    /// <summary>회원가입</summary>
    public int RegisterUser(MemberDo m)
    {
        using var conn = _db.OpenConnection();
        var exists = conn.QueryFirstOrDefault<string>(
            "SELECT userid FROM members WHERE userid = @Userid", new { m.Userid });
        if (exists != null) return -1;

        conn.Execute(
            @"INSERT INTO members (userid, passwd, name, nickname, email, status, ugrade,
              darkmode, language, googlescore, naverscore, daumscore)
              VALUES (@Userid, @Passwd, @Name, @Nickname, @Email, 'true', 1,
              false, 'ko', 0, 0, 0)",
            new { m.Userid, Passwd = GetMD5(m.Passwd), m.Name, m.Nickname, m.Email });
        return 1;
    }

    /// <summary>사용자 설정(다크모드, 언어) 조회</summary>
    public (bool darkMode, string lang) GetUserSettings(string userid)
    {
        using var conn = _db.OpenConnection();
        var row = conn.QueryFirstOrDefault(
            "SELECT darkmode, language FROM members WHERE userid = @userid",
            new { userid });
        if (row == null) return (false, "ko");
        bool dm = row.darkmode is bool b ? b : false;
        string lg = row.language?.ToString()?.Trim() ?? "ko";
        return (dm, lg);
    }

    /// <summary>다크모드·언어 설정 저장</summary>
    public void UpdateSettings(string userid, bool darkMode, string lang)
    {
        using var conn = _db.OpenConnection();
        // PostgreSQL boolean 타입 직접 사용
        conn.Execute(
            "UPDATE members SET darkmode = @dm, language = @lang WHERE userid = @userid",
            new { dm = darkMode, lang, userid });
    }

    /// <summary>닉네임 조회</summary>
    public string GetNickname(string uid)
    {
        using var conn = _db.OpenConnection();
        return conn.QueryFirstOrDefault<string>(
            "SELECT nickname FROM members WHERE userid = @uid", new { uid })?.Trim() ?? "사용자";
    }

    /// <summary>게시글 작성</summary>
    public int WriteBbs(string title, string contents, string author, string fileName, int fileSize)
    {
        try
        {
            using var conn = _db.OpenConnection();
            conn.Execute(
                @"INSERT INTO bbs (title, contents, author, uploadtime, hits, filename, filesize)
                  VALUES (@title, @contents, @author, NOW(), 0, @fileName, @fileSize)",
                new { title, contents, author, fileName, fileSize });
            return 1;
        }
        catch { return -1; }
    }

    /// <summary>게시글 수정</summary>
    public int UpdateBbs(int no, string title, string contents)
    {
        try
        {
            using var conn = _db.OpenConnection();
            return conn.Execute(
                "UPDATE bbs SET title = @title, contents = @contents WHERE no = @no",
                new { title, contents, no });
        }
        catch { return -1; }
    }

    /// <summary>내 게시글 목록 조회 (최신순)</summary>
    public IEnumerable<dynamic> GetMyPosts(string userID, int top = 5)
    {
        using var conn = _db.OpenConnection();
        return conn.Query(
            "SELECT no, title, uploadtime, hits FROM bbs WHERE author = @userID ORDER BY no DESC LIMIT @top",
            new { userID, top });
    }

    /// <summary>엔진별 누적 점수 조회</summary>
    public (double g, double n, double d) GetEngineScores(string userId)
    {
        using var conn = _db.OpenConnection();
        var row = conn.QueryFirstOrDefault(
            "SELECT googlescore, naverscore, daumscore FROM members WHERE userid = @userId",
            new { userId });
        if (row == null) return (0, 0, 0);
        double g = Convert.ToDouble(row.googlescore ?? 0);
        double n = Convert.ToDouble(row.naverscore ?? 0);
        double d = Convert.ToDouble(row.daumscore ?? 0);
        return (g, n, d);
    }

    /// <summary>엔진 점수 델타 적용 (하한 0 보장 — PostgreSQL GREATEST 사용)</summary>
    public void ApplyEngineScore(string userId, double gDelta, double nDelta, double dDelta)
    {
        using var conn = _db.OpenConnection();
        // PostgreSQL: GREATEST(val + delta, 0)  (MSSQL IIF/ISNULL 대체)
        conn.Execute(
            @"UPDATE members SET
              googlescore = GREATEST(COALESCE(googlescore, 0) + @gDelta, 0),
              naverscore  = GREATEST(COALESCE(naverscore,  0) + @nDelta, 0),
              daumscore   = GREATEST(COALESCE(daumscore,   0) + @dDelta, 0)
              WHERE userid = @userId",
            new { gDelta, nDelta, dDelta, userId });
    }

    /// <summary>엔진 점수 초기화</summary>
    public void ResetEngineScores(string userId)
    {
        using var conn = _db.OpenConnection();
        conn.Execute(
            "UPDATE members SET googlescore = 0, naverscore = 0, daumscore = 0 WHERE userid = @userId",
            new { userId });
    }

    /// <summary>비밀번호 변경</summary>
    public bool ChangePassword(string userId, string oldPw, string newPw)
    {
        string oldHash = GetMD5(oldPw);
        using var conn = _db.OpenConnection();
        var exists = conn.QueryFirstOrDefault<string>(
            "SELECT userid FROM members WHERE userid = @userId AND passwd = @oldHash",
            new { userId, oldHash });
        if (exists == null) return false;
        conn.Execute("UPDATE members SET passwd = @newHash WHERE userid = @userId",
            new { newHash = GetMD5(newPw), userId });
        return true;
    }

    /// <summary>회원 탈퇴 (계정 비활성화)</summary>
    public void Withdraw(string userId)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("UPDATE members SET status = 'false' WHERE userid = @userId", new { userId });
    }

    /// <summary>MD5 해시 (대문자 HEX 32자리)</summary>
    public static string GetMD5(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        byte[] hash = MD5.HashData(Encoding.ASCII.GetBytes(input));
        var sb = new StringBuilder(32);
        foreach (byte b in hash) sb.Append(b.ToString("X2"));
        return sb.ToString();
    }
}
