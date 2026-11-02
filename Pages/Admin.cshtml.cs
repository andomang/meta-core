using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>관리자 페이지 — test 계정만 접근 가능. 별점 평가 + API 사용량 확인.</summary>
public class AdminModel : PageModel
{
    /// <summary>test 계정이 아니면 메인으로 리다이렉트</summary>
    public IActionResult OnGet()
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid != "test") return Redirect("/");
        return Page();
    }
}
