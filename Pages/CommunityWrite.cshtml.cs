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

        // XSS 방지: 제목은 태그 전부 제거, 본문도 HTML 태그 전부 제거
        // (기존: script 태그만 제거 → <img onerror> 등 우회 가능했음)
        string title = System.Text.RegularExpressions.Regex.Replace(Title?.Trim() ?? "", "<.*?>", "");
        string contents = System.Text.RegularExpressions.Regex.Replace(Contents?.Trim() ?? "", "<.*?>", "");
        string fileName = "";
        int fileSize = 0;

        // 첨부파일 처리
        if (Upload != null && Upload.Length > 0)
        {
            // 파일 크기 제한 (10MB)
            if (Upload.Length > 10 * 1024 * 1024)
            {
                TempData["ToastMsg"] = "파일 크기는 10MB 이하만 허용됩니다.";
                TempData["ToastType"] = "error";
                return Page();
            }

            // 허용 확장자 검사
            string ext = Path.GetExtension(Upload.FileName).ToLowerInvariant();
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".zip", ".txt" };
            if (!allowed.Contains(ext))
            {
                TempData["ToastMsg"] = "허용되지 않는 파일 형식입니다.";
                TempData["ToastType"] = "error";
                return Page();
            }

            // 파일명 안전 처리 (경로 탐색 방지)
            fileName = Guid.NewGuid().ToString("N")[..8] + ext;
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
