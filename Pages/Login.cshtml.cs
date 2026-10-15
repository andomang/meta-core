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

    public LoginModel(Services.MemberDao dao) => _dao = dao;

    [BindProperty] public string LoginId { get; set; } = "";
    [BindProperty] public string LoginPw { get; set; } = "";

    // GET 요청 시 홈으로 리다이렉트
    public IActionResult OnGet() => Redirect("/");

    /// <summary>로그인 인증 처리</summary>
    public IActionResult OnPost()
    {
        if (string.IsNullOrWhiteSpace(LoginId) || string.IsNullOrWhiteSpace(LoginPw))
            return Redirect("/");

        // MD5 비밀번호 검증
        bool ok = _dao.Authenticate(LoginId, LoginPw);
        if (!ok)
        {
            TempData["ToastMsg"] = "아이디 또는 비밀번호가 올바르지 않습니다.";
            TempData["ToastType"] = "error";
            string failRef = Request.Headers["Referer"].ToString();
            return Redirect(string.IsNullOrEmpty(failRef) ? "/" : failRef);
        }

        // 세션에 사용자 정보 저장
        string nick = _dao.GetNickname(LoginId);
        HttpContext.Session.SetString("UserID", LoginId);
        HttpContext.Session.SetString("UserName", nick);

        // 사용자 설정(다크모드/언어) 세션 동기화
        var (dm, lg) = _dao.GetUserSettings(LoginId);
        HttpContext.Session.SetString("IsDark", dm.ToString());
        HttpContext.Session.SetString("Lang", lg);

        // 자동 로그인용 쿠키 설정 (7일 유효)
        var cookieOpt = new CookieOptions
        {
            Expires = DateTimeOffset.Now.AddDays(7),
            HttpOnly = true
        };
        Response.Cookies.Append("UserID", LoginId, cookieOpt);

        // 다크모드·언어 쿠키도 동기화
        Response.Cookies.Append("DarkMode", dm ? "1" : "0", cookieOpt);
        Response.Cookies.Append("Language", lg, cookieOpt);

        // 로그인 성공 알림
        TempData["ToastMsg"] = $"{nick}님, 환영합니다!";
        TempData["ToastType"] = "success";

        // Referer가 있으면 이전 페이지로, 없으면 홈으로
        string referer = Request.Headers["Referer"].ToString();
        return Redirect(string.IsNullOrEmpty(referer) ? "/" : referer);
    }
}
