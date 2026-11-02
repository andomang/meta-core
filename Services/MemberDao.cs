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

    /// <summary>아이디·비밀번호 검증 — BCrypt 우선, MD5 폴백 + 자동 마이그레이션</summary>
    public bool Authenticate(string id, string pw)
    {
        id = id.Trim().ToLowerInvariant();
        using var conn = _db.OpenConnection();
        var stored = conn.QueryFirstOrDefault<string>(
            "SELECT passwd FROM members WHERE LOWER(userid) = @id AND (TRIM(status) = 'true' OR TRIM(status) = '1')",
            new { id });
        if (stored == null) return false;

        stored = stored.Trim();

        // BCrypt 해시인지 확인 ($2a$, $2b$, $2y$ 접두사)
        if (stored.StartsWith("$2"))
        {
            return BCrypt.Net.BCrypt.Verify(pw, stored);
        }

        // MD5 레거시 — 검증 후 BCrypt로 자동 마이그레이션
        string md5Hash = GetMD5(pw);
        if (!string.Equals(stored, md5Hash, StringComparison.OrdinalIgnoreCase))
            return false;

        // MD5 인증 성공 → BCrypt 재해시 저장 (점진적 전환)
        string bcryptHash = BCrypt.Net.BCrypt.HashPassword(pw, workFactor: 11);
        conn.Execute("UPDATE members SET passwd = @bcryptHash WHERE userid = @id",
            new { bcryptHash, id });
        return true;
    }

    /// <summary>아이디 중복 확인 (사용 가능하면 true, 소문자 정규화 + LOWER 쿼리)</summary>
    public bool VerifyUserID(string id)
    {
        id = id.Trim().ToLowerInvariant();
        using var conn = _db.OpenConnection();
        var uid = conn.QueryFirstOrDefault<string>(
            "SELECT userid FROM members WHERE LOWER(userid) = @id", new { id });
        return uid == null;
    }

    /// <summary>회원가입 — 아이디 소문자 정규화 후 중복 검사·저장</summary>
    public int RegisterUser(MemberDo m)
    {
        m.Userid = m.Userid.Trim().ToLowerInvariant();
        using var conn = _db.OpenConnection();
        var exists = conn.QueryFirstOrDefault<string>(
            "SELECT userid FROM members WHERE LOWER(userid) = @uid", new { uid = m.Userid });
        if (exists != null) return -1;

        conn.Execute(
            @"INSERT INTO members (userid, passwd, name, nickname, email, status, ugrade,
              darkmode, language, googlescore, naverscore, daumscore, bravescore)
              VALUES (@Userid, @Passwd, @Name, @Nickname, @Email, 'true', 1,
              false, 'ko', 0, 0, 0, 0)",
            new { m.Userid, Passwd = BCrypt.Net.BCrypt.HashPassword(m.Passwd, workFactor: 11), m.Name, m.Nickname, m.Email });
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

    /// <summary>엔진별 누적 점수 조회 (4엔진: Google·Naver·Daum·Brave)</summary>
    public (double g, double n, double d, double b) GetEngineScores(string userId)
    {
        using var conn = _db.OpenConnection();
        var row = conn.QueryFirstOrDefault(
            "SELECT googlescore, naverscore, daumscore, COALESCE(bravescore, 0) AS bravescore FROM members WHERE userid = @userId",
            new { userId });
        if (row == null) return (0, 0, 0, 0);
        double g = Convert.ToDouble(row.googlescore ?? 0);
        double n = Convert.ToDouble(row.naverscore ?? 0);
        double d = Convert.ToDouble(row.daumscore ?? 0);
        double b = Convert.ToDouble(row.bravescore ?? 0);
        return (g, n, d, b);
    }

    /// <summary>엔진 점수 델타 적용 — 4엔진 (하한 0 보장)</summary>
    public void ApplyEngineScore(string userId, double gDelta, double nDelta, double dDelta, double bDelta = 0)
    {
        using var conn = _db.OpenConnection();
        conn.Execute(
            @"UPDATE members SET
              googlescore = GREATEST(COALESCE(googlescore, 0) + @gDelta, 0),
              naverscore  = GREATEST(COALESCE(naverscore,  0) + @nDelta, 0),
              daumscore   = GREATEST(COALESCE(daumscore,   0) + @dDelta, 0),
              bravescore  = GREATEST(COALESCE(bravescore,  0) + @bDelta, 0)
              WHERE userid = @userId",
            new { gDelta, nDelta, dDelta, bDelta, userId });
    }

    /// <summary>엔진 점수 초기화 (4엔진)</summary>
    public void ResetEngineScores(string userId)
    {
        using var conn = _db.OpenConnection();
        conn.Execute(
            "UPDATE members SET googlescore = 0, naverscore = 0, daumscore = 0, bravescore = 0 WHERE userid = @userId",
            new { userId });
    }

    /// <summary>비밀번호 변경 — BCrypt 우선, MD5 폴백 (Authenticate와 동일 로직)</summary>
    public bool ChangePassword(string userId, string oldPw, string newPw)
    {
        using var conn = _db.OpenConnection();
        var stored = conn.QueryFirstOrDefault<string>(
            "SELECT passwd FROM members WHERE userid = @userId",
            new { userId });
        if (stored == null) return false;

        stored = stored.Trim();
        bool match = stored.StartsWith("$2")
            ? BCrypt.Net.BCrypt.Verify(oldPw, stored)
            : string.Equals(stored, GetMD5(oldPw), StringComparison.OrdinalIgnoreCase);
        if (!match) return false;

        string newHash = BCrypt.Net.BCrypt.HashPassword(newPw, workFactor: 11);
        conn.Execute("UPDATE members SET passwd = @newHash WHERE userid = @userId",
            new { newHash, userId });
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
