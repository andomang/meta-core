using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>게시글 수정 페이지 — 본인 게시글만 수정 가능, 파일 업로드/삭제 지원</summary>
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
    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public bool DeleteFile { get; set; }

    // 기존 첨부파일 정보 (뷰에서 표시용)
    public string ExistingFileName { get; set; } = "";
    public int ExistingFileSize { get; set; }

    /// <summary>기존 게시글 데이터 로드</summary>
    public IActionResult OnGet(int no)
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return Redirect("/");
        if (no <= 0) return RedirectToPage("Community");

        PostNo = no;

        using var conn = _db.OpenConnection();
        var row = conn.QueryFirstOrDefault(
            "SELECT no, title, contents, author, filename, filesize FROM bbs WHERE no = @no", new { no });

        if (row == null) return RedirectToPage("Community");

        // 본인 글인지 검증
        string author = ((string)(row.author ?? "")).Trim();
        if (author != uid) return RedirectToPage("Community");

        Title = row.title ?? "";
        Contents = row.contents ?? "";
        ExistingFileName = ((string)(row.filename ?? "")).Trim();
        ExistingFileSize = Convert.ToInt32(row.filesize ?? 0);
        return Page();
    }

    /// <summary>수정 내용 저장 — 소유권 재검증 + 파일 수정/삭제 처리</summary>
    public IActionResult OnPost(int PostNo)
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return Redirect("/");

        // 소유권 재검증 (POST 조작으로 타인 게시글 수정 방지)
        using var conn = _db.OpenConnection();
        var row = conn.QueryFirstOrDefault(
            "SELECT author, filename FROM bbs WHERE no = @PostNo", new { PostNo });
        if (row == null || ((string)(row.author ?? "")).Trim() != uid)
        {
            TempData["ToastMsg"] = "수정 권한이 없습니다.";
            TempData["ToastType"] = "error";
            return RedirectToPage("Community");
        }

        // XSS 방지: 제목·본문 모두 HTML 태그 전부 제거
        string title = System.Text.RegularExpressions.Regex.Replace(Title?.Trim() ?? "", "<.*?>", "");
        string contents = System.Text.RegularExpressions.Regex.Replace(Contents?.Trim() ?? "", "<.*?>", "");

        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(contents))
            return OnGet(PostNo);

        string existingFile = ((string)(row.filename ?? "")).Trim();
        string uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "bbs");
        string newFileName = existingFile;
        int newFileSize = 0;

        // 파일 삭제 요청 처리
        if (DeleteFile && !string.IsNullOrEmpty(existingFile))
        {
            string delPath = Path.Combine(uploadDir, Path.GetFileName(existingFile));
            if (System.IO.File.Exists(delPath))
                System.IO.File.Delete(delPath);
            newFileName = "";
            newFileSize = 0;
        }

        // 새 파일 업로드 처리
        if (Upload != null && Upload.Length > 0)
        {
            // 파일 크기 제한 (10MB)
            if (Upload.Length > 10 * 1024 * 1024)
            {
                TempData["ToastMsg"] = "파일 크기는 10MB 이하만 허용됩니다.";
                TempData["ToastType"] = "error";
                return OnGet(PostNo);
            }

            // 허용 확장자 검사
            string ext = Path.GetExtension(Upload.FileName).ToLowerInvariant();
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".zip", ".txt" };
            if (!allowed.Contains(ext))
            {
                TempData["ToastMsg"] = "허용되지 않는 파일 형식입니다.";
                TempData["ToastType"] = "error";
                return OnGet(PostNo);
            }

            // 기존 파일 삭제 (새 파일로 교체)
            if (!string.IsNullOrEmpty(existingFile))
            {
                string oldPath = Path.Combine(uploadDir, Path.GetFileName(existingFile));
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }

            // 새 파일 저장
            if (!Directory.Exists(uploadDir))
                Directory.CreateDirectory(uploadDir);

            newFileName = Guid.NewGuid().ToString("N")[..8] + ext;
            newFileSize = (int)Upload.Length;

            string filePath = Path.Combine(uploadDir, newFileName);
            using var stream = new FileStream(filePath, FileMode.Create);
            Upload.CopyTo(stream);
        }
        else if (!DeleteFile)
        {
            // 파일 변경 없음 — 기존 파일 유지
            var sizeRow = conn.QueryFirstOrDefault<int?>(
                "SELECT filesize FROM bbs WHERE no = @PostNo", new { PostNo });
            newFileSize = sizeRow ?? 0;
        }

        // DB 업데이트 (제목, 본문, 파일 정보)
        conn.Execute(
            "UPDATE bbs SET title = @title, contents = @contents, filename = @fn, filesize = @fs WHERE no = @PostNo",
            new { title, contents, fn = newFileName, fs = newFileSize, PostNo });

        TempData["ToastMsg"] = "게시글이 수정되었습니다.";
        TempData["ToastType"] = "success";
        return Redirect($"/CommunityView?no={PostNo}");
    }
}
