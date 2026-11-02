using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>사용자 대시보드 — 개인 검색 통계, 키워드 클라우드, 활동 요약</summary>
public class DashboardModel : PageModel
{
    public readonly Services.Lang Lang;
    public DashboardModel(Services.Lang lang) => Lang = lang;

    public void OnGet() { }
}
