using Microsoft.AspNetCore.Mvc.RazorPages;
using MetaSearch.Services;

namespace MetaSearch.Pages;

/// <summary>홈 페이지 모델 — 검색 입력 + 바로가기 카드</summary>
public class IndexModel : PageModel
{
    private readonly Lang _lang;

    public IndexModel(Lang lang) => _lang = lang;

    // 다국어 텍스트 바인딩
    public string SubtitleText => _lang.Get("home.subtitle");
    public string PlaceholderText => _lang.Get("home.placeholder");
    public string CommunityText => _lang.Get("home.community");
    public string SettingsText => _lang.Get("home.settings");

    public void OnGet() { }
}
