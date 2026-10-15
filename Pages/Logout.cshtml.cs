using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>로그아웃 처리 — 세션·쿠키 클리어 후 홈으로 이동</summary>
public class LogoutModel : PageModel
{
    public IActionResult OnGet()
    {
        // 세션 전체 클리어
        HttpContext.Session.Clear();

        // 자동 로그인 쿠키 삭제
        Response.Cookies.Delete("UserID");

        return Redirect("/");
    }
}
