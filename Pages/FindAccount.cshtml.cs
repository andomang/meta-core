using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>아이디/비밀번호 찾기 페이지 — 이메일 인증 기반 계정 복구</summary>
public class FindAccountModel : PageModel
{
    public readonly Services.Lang Lang;

    // 초기 탭 (id 또는 pw)
    public string InitialTab { get; set; } = "id";

    public FindAccountModel(Services.Lang lang) => Lang = lang;

    public void OnGet()
    {
        string? tab = Request.Query["tab"].ToString();
        InitialTab = tab == "pw" ? "pw" : "id";
    }
}
