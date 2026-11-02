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
    public double BravePct { get; set; }
    public List<dynamic> RecentSearches { get; set; } = new();
    public List<dynamic> MyPosts { get; set; } = new();

    /// <summary>비밀번호 변경 POST 핸들러 — 비밀번호 정책 검증 포함</summary>
    public IActionResult OnPostChangePw()
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return new JsonResult(new { result = "unauthorized" });

        string oldPw = Request.HasFormContentType && Request.Form.ContainsKey("oldPw")
            ? Request.Form["oldPw"].ToString() : "";
        string newPw = Request.HasFormContentType && Request.Form.ContainsKey("newPw")
            ? Request.Form["newPw"].ToString() : "";

        // 서버측 비밀번호 정책 검증 (8자 이상, 영문+숫자+특수문자)
        if (newPw.Length < 8 ||
            !System.Text.RegularExpressions.Regex.IsMatch(newPw, @"[A-Za-z]") ||
            !System.Text.RegularExpressions.Regex.IsMatch(newPw, @"[0-9]") ||
            !System.Text.RegularExpressions.Regex.IsMatch(newPw, @"[^A-Za-z0-9]"))
            return new JsonResult(new { result = "weak_password" });

        bool ok = _memberDao.ChangePassword(uid, oldPw, newPw);
        return new JsonResult(new { result = ok ? "ok" : "wrongPw" });
    }

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

        // 엔진 점수 비율 (4엔진)
        var (g, n, d, bv) = _memberDao.GetEngineScores(uid);
        double total = g + n + d + bv;
        if (total > 0)
        {
            GooglePct = g / total * 100;
            NaverPct = n / total * 100;
            DaumPct = d / total * 100;
            BravePct = bv / total * 100;
        }
        else
        {
            GooglePct = NaverPct = DaumPct = BravePct = 25.0;
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

    /// <summary>비밀번호 변경 — 비밀번호 정책 검증 포함</summary>
    private IActionResult OnChangePw(string uid)
    {
        // POST body 또는 쿼리스트링 둘 다 지원 (하위 호환)
        string oldPw = Request.HasFormContentType && Request.Form.ContainsKey("oldPw")
            ? Request.Form["oldPw"].ToString()
            : Request.Query["oldPw"].ToString();
        string newPw = Request.HasFormContentType && Request.Form.ContainsKey("newPw")
            ? Request.Form["newPw"].ToString()
            : Request.Query["newPw"].ToString();

        // 서버측 비밀번호 정책 검증 (8자 이상, 영문+숫자+특수문자)
        if (newPw.Length < 8 ||
            !System.Text.RegularExpressions.Regex.IsMatch(newPw, @"[A-Za-z]") ||
            !System.Text.RegularExpressions.Regex.IsMatch(newPw, @"[0-9]") ||
            !System.Text.RegularExpressions.Regex.IsMatch(newPw, @"[^A-Za-z0-9]"))
            return new JsonResult(new { result = "weak_password" });

        bool ok = _memberDao.ChangePassword(uid, oldPw, newPw);
        return new JsonResult(new { result = ok ? "ok" : "wrongPw" });
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

        // 쿠키 동기화 (보안 옵션 포함)
        Response.Cookies.Append("DarkMode", newVal ? "1" : "0",
            new CookieOptions { Expires = DateTimeOffset.Now.AddDays(30), Secure = true, SameSite = SameSiteMode.Lax });

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
            new CookieOptions { Expires = DateTimeOffset.Now.AddDays(30), Secure = true, SameSite = SameSiteMode.Lax });

        return new JsonResult(new { result = "ok" });
    }

    /// <summary>프로필 정보 수정 — XSS 방지 태그 제거 + 길이 제한</summary>
    private IActionResult OnUpdateProfile(string uid)
    {
        string name = System.Text.RegularExpressions.Regex.Replace(
            Request.Query["name"].ToString().Trim(), "<.*?>", "");
        string nick = System.Text.RegularExpressions.Regex.Replace(
            Request.Query["nick"].ToString().Trim(), "<.*?>", "");
        string email = Request.Query["email"].ToString().Trim();

        // 길이 제한 (20자)
        if (name.Length > 20) name = name[..20];
        if (nick.Length > 20) nick = nick[..20];

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
