using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>
/// 로그인 처리 페이지 (POST 전용).
/// _Layout.cshtml의 로그인 모달 폼이 이 페이지로 POST합니다.
/// 인증 성공 시 세션+쿠키 설정 후 이전 페이지로 리다이렉트.
/// </summary>
public class LoginModel : PageModel
{
    private readonly Services.MemberDao _dao;
    private readonly Services.DbService _db;

    public LoginModel(Services.MemberDao dao, Services.DbService db) { _dao = dao; _db = db; }

    [BindProperty] public string LoginId { get; set; } = "";
    [BindProperty] public string LoginPw { get; set; } = "";

    // GET 요청 시 홈으로 리다이렉트
    public IActionResult OnGet() => Redirect("/");

    // 로그인 시도 추적 (IP별 실패 횟수 + 잠금 시각)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Fails, DateTime LockUntil)>
        _loginAttempts = new();

    /// <summary>로그인 인증 처리 — 브루트포스 방지 레이트 리미팅</summary>
    public IActionResult OnPost()
    {
        if (string.IsNullOrWhiteSpace(LoginId) || string.IsNullOrWhiteSpace(LoginPw))
            return Redirect("/");

        // 브루트포스 방지: IP당 5회 실패 시 5분간 차단
        string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (_loginAttempts.TryGetValue(clientIp, out var attempt) && DateTime.UtcNow < attempt.LockUntil)
        {
            TempData["ToastMsg"] = "로그인 시도가 너무 많습니다. 5분 후 다시 시도해주세요.";
            TempData["ToastType"] = "error";
            string lockRef = Request.Headers["Referer"].ToString();
            return Redirect(string.IsNullOrEmpty(lockRef) ? "/" : lockRef);
        }

        // 아이디 소문자 정규화 + 공백 제거
        LoginId = LoginId.Trim().ToLowerInvariant();

        bool ok = _dao.Authenticate(LoginId, LoginPw);
        if (!ok)
        {
            // 실패 횟수 증가, 5회 초과 시 5분 잠금
            var current = _loginAttempts.GetOrAdd(clientIp, _ => (0, DateTime.MinValue));
            int fails = current.Fails + 1;
            DateTime lockUntil = fails >= 5 ? DateTime.UtcNow.AddMinutes(5) : DateTime.MinValue;
            _loginAttempts[clientIp] = (fails, lockUntil);

            string msg = fails >= 5
                ? "로그인 시도가 너무 많습니다. 5분 후 다시 시도해주세요."
                : "아이디 또는 비밀번호가 올바르지 않습니다.";
            TempData["ToastMsg"] = msg;
            TempData["ToastType"] = "error";
            string failRef = Request.Headers["Referer"].ToString();
            return Redirect(string.IsNullOrEmpty(failRef) ? "/" : failRef);
        }

        // 로그인 성공 시 실패 기록 초기화
        _loginAttempts.TryRemove(clientIp, out _);

        // 세션에 사용자 정보 저장
        string nick = _dao.GetNickname(LoginId);
        HttpContext.Session.SetString("UserID", LoginId);
        HttpContext.Session.SetString("UserName", nick);

        // 사용자 설정(다크모드/언어) 세션 동기화
        var (dm, lg) = _dao.GetUserSettings(LoginId);
        HttpContext.Session.SetString("IsDark", dm.ToString());
        HttpContext.Session.SetString("Lang", lg);

        // 자동 로그인용 쿠키 설정 (7일 유효, 보안 강화)
        var cookieOpt = new CookieOptions
        {
            Expires = DateTimeOffset.Now.AddDays(7),
            HttpOnly = true,
            Secure = true,              // HTTPS에서만 전송
            SameSite = SameSiteMode.Lax // CSRF 방지
        };
        Response.Cookies.Append("UserID", LoginId, cookieOpt);

        // 다크모드·언어 쿠키도 동기화
        Response.Cookies.Append("DarkMode", dm ? "1" : "0", cookieOpt);
        Response.Cookies.Append("Language", lg, cookieOpt);

        // 접속 IP + 마지막 로그인 시간 기록
        string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        using (var conn = _db.OpenConnection())
        {
            Dapper.SqlMapper.Execute(conn,
                "UPDATE members SET last_ip = @ip, last_login = NOW() WHERE userid = @LoginId",
                new { ip, LoginId });
        }

        // 로그인 성공 알림
        TempData["ToastMsg"] = $"{nick}님, 환영합니다!";
        TempData["ToastType"] = "success";

        // Referer가 있으면 이전 페이지로, 없으면 홈으로
        string referer = Request.Headers["Referer"].ToString();
        return Redirect(string.IsNullOrEmpty(referer) ? "/" : referer);
    }
}
