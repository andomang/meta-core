using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>북마크 관리 페이지 — 로그인 필요, 실제 데이터는 API로 조회</summary>
public class BookmarksModel : PageModel
{
    public readonly Services.Lang Lang;
    public BookmarksModel(Services.Lang lang) => Lang = lang;

    public void OnGet() { }
}
