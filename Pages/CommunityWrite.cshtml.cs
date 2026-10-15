using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>게시글 작성 페이지 — 제목·본문·파일 업로드 후 DB INSERT</summary>
public class CommunityWriteModel : PageModel
{
    private readonly Services.MemberDao _dao;
    public readonly Services.Lang Lang;

    public CommunityWriteModel(Services.MemberDao dao, Services.Lang lang)
    {
        _dao = dao;
        Lang = lang;
    }

    [BindProperty] public string Title { get; set; } = "";
    [BindProperty] public string Contents { get; set; } = "";
    [BindProperty] public IFormFile? Upload { get; set; }

    /// <summary>비로그인 접근 차단</summary>
    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetString("UserID") == null)
            return Redirect("/");
        return Page();
    }

    /// <summary>게시글 저장 처리</summary>
    public IActionResult OnPost()
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return Redirect("/");

        string title = Title?.Trim() ?? "";
        string contents = Contents?.Trim() ?? "";
        string fileName = "";
        int fileSize = 0;

        // 첨부파일 처리
        if (Upload != null && Upload.Length > 0)
        {
            fileName = Path.GetFileName(Upload.FileName);
            fileSize = (int)Upload.Length;

            // 업로드 폴더 생성
            string uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "bbs");
            if (!Directory.Exists(uploadDir))
                Directory.CreateDirectory(uploadDir);

            string filePath = Path.Combine(uploadDir, fileName);
            using var stream = new FileStream(filePath, FileMode.Create);
            Upload.CopyTo(stream);
        }

        // DB에 게시글 INSERT
        int result = _dao.WriteBbs(title, contents, uid, fileName, fileSize);
        if (result > 0)
        {
            TempData["ToastMsg"] = "게시글이 등록되었습니다.";
            TempData["ToastType"] = "success";
            return Redirect("/Community");
        }

        TempData["ToastMsg"] = "게시글 등록에 실패했습니다.";
        TempData["ToastType"] = "error";
        return Page();
    }
}
