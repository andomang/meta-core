using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>
/// 설정 페이지 — 테마·언어·프로필 수정, 검색 통계, 엔진 점수, 검색 기록 관리.
/// AJAX 핸들러: changePw, deleteHistory, resetScores, updateProfile, withdraw.
/// </summary>
public class SettingsModel : PageModel
{
    private readonly Services.DbService _db;
    private readonly Services.MemberDao _memberDao;
    private readonly Services.SearchDao _searchDao;
    public readonly Services.Lang Lang;

    public SettingsModel(Services.DbService db, Services.MemberDao memberDao,
        Services.SearchDao searchDao, Services.Lang lang)
    {
        _db = db; _memberDao = memberDao; _searchDao = searchDao; Lang = lang;
    }

    // 뷰 데이터
    public string UserName { get; set; } = "";
    public string UserNick { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public bool IsDark { get; set; }
    public string CurrentLang { get; set; } = "ko";
    public int TotalSearches { get; set; }
    public string TopKeyword { get; set; } = "-";
    public double GooglePct { get; set; }
    public double NaverPct { get; set; }
    public double DaumPct { get; set; }
    public List<dynamic> RecentSearches { get; set; } = new();
    public List<dynamic> MyPosts { get; set; } = new();

    /// <summary>설정 페이지 로드</summary>
    public IActionResult OnGet(string? handler = null)
    {
        // AJAX 핸들러 분기
        if (handler != null) return HandleAjax(handler);

        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return Redirect("/");

        // 사용자 기본 정보
        using var conn = _db.OpenConnection();
        // PostgreSQL 소문자로 컬럼 명시 (대소문자 혼용 방지)
        var user = conn.QueryFirstOrDefault(
            "SELECT name, nickname, email, darkmode, language FROM members WHERE userid=@uid",
            new { uid });
        if (user != null)
        {
            // PostgreSQL 소문자 컬럼명으로 접근
            UserName = ((string)(user.name ?? "")).Trim();
            UserNick = ((string)(user.nickname ?? "")).Trim();
            UserEmail = ((string)(user.email ?? "")).Trim();
            IsDark = user.darkmode is bool b ? b : false;
            CurrentLang = ((string)(user.language ?? "ko")).Trim();
        }

        // 검색 통계
        TotalSearches = _searchDao.GetTotalSearchCount(uid);
        TopKeyword = _searchDao.GetTopKeyword(uid);

        // 엔진 점수 비율
        var (g, n, d) = _memberDao.GetEngineScores(uid);
        double total = g + n + d;
        if (total > 0)
        {
            GooglePct = g / total * 100;
            NaverPct = n / total * 100;
            DaumPct = d / total * 100;
        }
        else
        {
            GooglePct = NaverPct = DaumPct = 33.3;
        }

        // 최근 검색어
        RecentSearches = _searchDao.GetRecentSearches(uid, 10).ToList();

        // 내 게시글
        MyPosts = _memberDao.GetMyPosts(uid, 5).ToList();

        return Page();
    }

    /// <summary>AJAX 요청 분기 처리</summary>
    private IActionResult HandleAjax(string handler)
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return new JsonResult(new { result = "unauthorized" });

        switch (handler)
        {
            case "ChangePw":
                return OnChangePw(uid);
            case "DeleteHistory":
                return OnDeleteHistory(uid);
            case "ResetScores":
                return OnResetScores(uid);
            case "ToggleDark":
                return OnToggleDark(uid);
            case "SetLang":
                return OnSetLang(uid);
            case "UpdateProfile":
                return OnUpdateProfile(uid);
            case "Withdraw":
                return OnWithdraw(uid);
            default:
                return new JsonResult(new { result = "unknown" });
        }
    }

    /// <summary>비밀번호 변경</summary>
    private IActionResult OnChangePw(string uid)
    {
        string oldPw = Request.Query["oldPw"].ToString();
        string newPw = Request.Query["newPw"].ToString();

        using var conn = _db.OpenConnection();
        string oldHash = Services.MemberDao.GetMD5(oldPw);
        var check = conn.QueryFirstOrDefault<string>(
            "SELECT userid FROM members WHERE userid=@uid AND passwd=@oldHash",
            new { uid, oldHash });
        if (check == null)
            return new JsonResult(new { result = "wrongPw" });

        string newHash = Services.MemberDao.GetMD5(newPw);
        conn.Execute("UPDATE members SET passwd=@newHash WHERE userid=@uid",
            new { newHash, uid });

        return new JsonResult(new { result = "ok" });
    }

    /// <summary>기간별 검색 기록 삭제</summary>
    private IActionResult OnDeleteHistory(string uid)
    {
        string period = Request.Query["period"].ToString();
        _searchDao.DeleteSearchHistoryByPeriod(uid, period);
        return new JsonResult(new { result = "ok" });
    }

    /// <summary>엔진 점수 초기화</summary>
    private IActionResult OnResetScores(string uid)
    {
        _memberDao.ResetEngineScores(uid);
        return new JsonResult(new { result = "ok" });
    }

    /// <summary>다크모드 토글</summary>
    private IActionResult OnToggleDark(string uid)
    {
        bool current = HttpContext.Session.GetString("IsDark") == "True";
        bool newVal = !current;
        string lang = HttpContext.Session.GetString("Lang") ?? "ko";

        _memberDao.UpdateSettings(uid, newVal, lang);
        HttpContext.Session.SetString("IsDark", newVal.ToString());

        // 쿠키 동기화
        Response.Cookies.Append("DarkMode", newVal ? "1" : "0",
            new CookieOptions { Expires = DateTimeOffset.Now.AddDays(30) });

        return new JsonResult(new { result = "ok", isDark = newVal });
    }

    /// <summary>언어 변경</summary>
    private IActionResult OnSetLang(string uid)
    {
        string lang = Request.Query["lang"].ToString();
        if (lang != "ko" && lang != "en") lang = "ko";

        bool dm = HttpContext.Session.GetString("IsDark") == "True";
        _memberDao.UpdateSettings(uid, dm, lang);
        HttpContext.Session.SetString("Lang", lang);

        Response.Cookies.Append("Language", lang,
            new CookieOptions { Expires = DateTimeOffset.Now.AddDays(30) });

        return new JsonResult(new { result = "ok" });
    }

    /// <summary>프로필 정보 수정</summary>
    private IActionResult OnUpdateProfile(string uid)
    {
        string name = Request.Query["name"].ToString().Trim();
        string nick = Request.Query["nick"].ToString().Trim();
        string email = Request.Query["email"].ToString().Trim();

        using var conn = _db.OpenConnection();
        conn.Execute(
            "UPDATE members SET name=@name, nickname=@nick, email=@email WHERE userid=@uid",
            new { name, nick, email, uid });

        HttpContext.Session.SetString("UserName", nick);
        return new JsonResult(new { result = "ok" });
    }

    /// <summary>회원 탈퇴 (모든 데이터 삭제)</summary>
    private IActionResult OnWithdraw(string uid)
    {
        using var conn = _db.OpenConnection();

        // 관련 데이터 순차 삭제
        // FK 순서에 맞춰 관련 데이터 삭제
        conn.Execute("DELETE FROM notifications WHERE userid=@uid OR actorid=@uid", new { uid });
        conn.Execute("DELETE FROM bbscomment WHERE author=@uid", new { uid });
        conn.Execute("DELETE FROM bbslike WHERE userid=@uid", new { uid });
        conn.Execute("DELETE FROM searchhistory WHERE userid=@uid", new { uid });
        conn.Execute("DELETE FROM bbs WHERE author=@uid", new { uid });
        conn.Execute("DELETE FROM members WHERE userid=@uid", new { uid });

        // 세션·쿠키 클리어
        HttpContext.Session.Clear();
        Response.Cookies.Delete("UserID");
        Response.Cookies.Delete("DarkMode");
        Response.Cookies.Delete("Language");

        return new JsonResult(new { result = "ok" });
    }
}
