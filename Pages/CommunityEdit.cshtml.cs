using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>게시글 수정 페이지 — 본인 게시글만 수정 가능</summary>
public class CommunityEditModel : PageModel
{
    private readonly Services.DbService _db;
    private readonly Services.MemberDao _dao;
    public readonly Services.Lang Lang;

    public CommunityEditModel(Services.DbService db, Services.MemberDao dao, Services.Lang lang)
    {
        _db = db;
        _dao = dao;
        Lang = lang;
    }

    public int PostNo { get; set; }
    [BindProperty] public string Title { get; set; } = "";
    [BindProperty] public string Contents { get; set; } = "";

    /// <summary>기존 게시글 데이터 로드</summary>
    public IActionResult OnGet(int no)
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return Redirect("/");
        if (no <= 0) return RedirectToPage("Community");

        PostNo = no;

        using var conn = _db.OpenConnection();
        var row = conn.QueryFirstOrDefault(
            "SELECT no, title, contents, author FROM bbs WHERE no = @no", new { no });

        if (row == null) return RedirectToPage("Community");

        // 본인 글인지 검증
        string author = ((string)(row.author ?? "")).Trim();
        if (author != uid) return RedirectToPage("Community");

        Title = row.title ?? "";
        Contents = row.contents ?? "";
        return Page();
    }

    /// <summary>수정 내용 저장</summary>
    public IActionResult OnPost(int PostNo)
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return Redirect("/");

        string title = Title?.Trim() ?? "";
        string contents = Contents?.Trim() ?? "";

        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(contents))
            return OnGet(PostNo);

        _dao.UpdateBbs(PostNo, title, contents);
        TempData["ToastMsg"] = "게시글이 수정되었습니다.";
        TempData["ToastType"] = "success";
        return Redirect($"/CommunityView?no={PostNo}");
    }
}
