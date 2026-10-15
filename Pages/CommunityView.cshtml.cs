using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace MetaSearch.Pages;

/// <summary>
/// 게시글 상세 보기 페이지.
/// 좋아요/댓글 AJAX → OnGetToggleLike, OnGetAddComment, OnGetDeleteComment 핸들러 처리.
/// </summary>
public class CommunityViewModel : PageModel
{
    private readonly Services.DbService _db;
    public readonly Services.Lang Lang;

    public CommunityViewModel(Services.DbService db, Services.Lang lang)
    {
        _db = db;
        Lang = lang;
    }

    // 뷰 데이터
    public int PostNo { get; set; }
    public PostData? Post { get; set; }
    public string AvatarInitial { get; set; } = "?";
    public bool IsOwner { get; set; }
    public bool IsLoggedIn { get; set; }
    public bool IsLiked { get; set; }
    public string MyAvatar { get; set; } = "?";
    public List<CommentData> Comments { get; set; } = new();

    /// <summary>게시글 상세 + 댓글 목록 로드</summary>
    public IActionResult OnGet(int no, string? handler = null)
    {
        if (no <= 0) return RedirectToPage("Community");
        PostNo = no;

        string? uid = HttpContext.Session.GetString("UserID");
        IsLoggedIn = uid != null;

        using var conn = _db.OpenConnection();

        // 조회수 증가
        conn.Execute("UPDATE bbs SET hits = hits + 1 WHERE no = @no", new { no });

        // 게시글 조회
        var row = conn.QueryFirstOrDefault(
            @"SELECT b.*, m.nickname FROM bbs b
              INNER JOIN members m ON b.author = m.userid
              WHERE b.no = @no", new { no });

        if (row == null) return RedirectToPage("Community");

        // PostgreSQL은 컬럼명을 소문자로 반환 → 모두 소문자로 접근
        string nick = ((string)(row.nickname ?? "")).Trim();
        Post = new PostData
        {
            Title = row.title ?? "",
            Contents = System.Net.WebUtility.HtmlEncode(row.contents ?? ""),
            AuthorNick = nick,
            Date = row.uploadtime != null ? ((DateTime)row.uploadtime).ToString("yyyy.MM.dd HH:mm") : "",
            Hits = Convert.ToInt32(row.hits ?? 0),
            FileName = row.filename ?? "",
            FileSize = Convert.ToInt32(row.filesize ?? 0),
            Author = ((string)(row.author ?? "")).Trim()
        };
        AvatarInitial = nick.Length > 0 ? nick[..1].ToUpper() : "?";

        // 소유자 여부
        IsOwner = uid != null && uid == Post.Author;

        // 내 아바타
        if (uid != null)
        {
            string myNick = HttpContext.Session.GetString("UserName") ?? "?";
            MyAvatar = myNick.Length > 0 ? myNick[..1].ToUpper() : "?";
        }

        // 좋아요 여부 — COUNT(*)는 bigint, ExecuteScalar<long>으로 받아 비교
        if (uid != null)
        {
            long lc = conn.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM bbslike WHERE bbsno=@no AND userid=@uid", new { no, uid });
            IsLiked = lc > 0;
        }

        // 댓글 목록
        var cRows = conn.Query(
            @"SELECT c.commentid, c.author, c.content, c.createdat,
                     m.nickname AS authornick
              FROM bbscomment c
              INNER JOIN members m ON c.author = m.userid
              WHERE c.bbsno = @no ORDER BY c.commentid ASC", new { no });

        foreach (var c in cRows)
        {
            string cNick = ((string)(c.authornick ?? "")).Trim();
            Comments.Add(new CommentData
            {
                CommentId = Convert.ToInt32(c.commentid ?? 0),
                AuthorNick = cNick,
                Avatar = cNick.Length > 0 ? cNick[..1].ToUpper() : "?",
                Content = c.content ?? "",
                CreatedAt = c.createdat != null ? ((DateTime)c.createdat).ToString("yyyy.MM.dd HH:mm") : "",
                CanDelete = uid != null && uid == ((string)(c.author ?? "")).Trim()
            });
        }

        return Page();
    }

    /// <summary>좋아요 상태 조회 (AJAX)</summary>
    public JsonResult OnGetGetLike(int no)
    {
        string? uid = HttpContext.Session.GetString("UserID");
        using var conn = _db.OpenConnection();
        // COUNT(*)는 bigint — long으로 받아 int 변환
        int count = Convert.ToInt32(conn.ExecuteScalar<long>("SELECT COUNT(*) FROM bbslike WHERE bbsno=@no", new { no }));
        bool liked = false;
        if (uid != null)
            liked = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM bbslike WHERE bbsno=@no AND userid=@uid", new { no, uid }) > 0;
        return new JsonResult(new { count, liked });
    }

    /// <summary>좋아요 토글 (AJAX)</summary>
    public JsonResult OnGetToggleLike(int no)
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return new JsonResult(new { result = "unauthorized" });

        using var conn = _db.OpenConnection();
        long exists = conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM bbslike WHERE bbsno=@no AND userid=@uid", new { no, uid });

        bool liked;
        if (exists > 0)
        {
            conn.Execute("DELETE FROM bbslike WHERE bbsno=@no AND userid=@uid", new { no, uid });
            liked = false;
        }
        else
        {
            conn.Execute("INSERT INTO bbslike (bbsno, userid) VALUES (@no, @uid)", new { no, uid });
            liked = true;
            // 좋아요 알림 생성 (게시글 작성자에게, 자신 제외)
            var author = conn.QueryFirstOrDefault<string>("SELECT author FROM bbs WHERE no=@no", new { no });
            if (author != null && author.Trim() != uid)
                conn.Execute("INSERT INTO notifications (userid,actorid,bbsno,type) VALUES (@to,@uid,@no,'like')",
                    new { to = author.Trim(), uid, no });
        }
        int count = Convert.ToInt32(conn.ExecuteScalar<long>("SELECT COUNT(*) FROM bbslike WHERE bbsno=@no", new { no }));
        return new JsonResult(new { count, liked });
    }

    /// <summary>댓글 추가 (AJAX)</summary>
    public JsonResult OnGetAddComment(int no, string content)
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return new JsonResult(new { result = "unauthorized" });
        if (string.IsNullOrWhiteSpace(content)) return new JsonResult(new { result = "empty" });

        using var conn = _db.OpenConnection();

        // 댓글 INSERT 후 RETURNING으로 새 ID 반환 (PostgreSQL — 컬럼명은 commentid)
        int commentID = conn.ExecuteScalar<int>(
            "INSERT INTO bbscomment (bbsno,author,content) VALUES (@no,@uid,@content) RETURNING commentid",
            new { no, uid, content });

        // 댓글 알림 생성
        var author = conn.QueryFirstOrDefault<string>("SELECT author FROM bbs WHERE no=@no", new { no });
        if (author != null && author.Trim() != uid)
            conn.Execute("INSERT INTO notifications (userid,actorid,bbsno,commentid,type) VALUES (@to,@uid,@no,@commentID,'comment')",
                new { to = author.Trim(), uid, no, commentID });

        // 닉네임 조회
        string nick = conn.QueryFirstOrDefault<string>(
            "SELECT nickname FROM members WHERE userid=@uid", new { uid })?.Trim() ?? "?";
        string avatarHtml = nick.Length > 0 ? nick[..1].ToUpper() : "?";
        int totalCount = Convert.ToInt32(conn.ExecuteScalar<long>("SELECT COUNT(*) FROM bbscomment WHERE bbsno=@no", new { no }));

        return new JsonResult(new
        {
            result = "ok",
            commentID,
            nick,
            content,
            createdAt = DateTime.Now.ToString("yyyy.MM.dd HH:mm"),
            avatarHtml,
            totalCount
        });
    }

    /// <summary>댓글 삭제 (AJAX)</summary>
    public JsonResult OnGetDeleteComment(int commentID, int no)
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return new JsonResult(new { result = "unauthorized" });

        using var conn = _db.OpenConnection();
        conn.Execute("DELETE FROM bbscomment WHERE commentid=@commentID AND author=@uid", new { commentID, uid });
        int totalCount = Convert.ToInt32(conn.ExecuteScalar<long>("SELECT COUNT(*) FROM bbscomment WHERE bbsno=@no", new { no }));
        return new JsonResult(new { result = "ok", totalCount });
    }

    /// <summary>첨부파일 다운로드 (GET)</summary>
    public IActionResult OnGetDownload(int no)
    {
        using var conn = _db.OpenConnection();
        // 파일명 조회
        var row = conn.QueryFirstOrDefault(
            "SELECT filename FROM bbs WHERE no = @no", new { no });
        if (row == null) return NotFound();

        string fileName = ((string)(row.filename ?? "")).Trim();
        if (string.IsNullOrEmpty(fileName)) return NotFound();

        // 업로드 경로에서 파일 제공
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "bbs", fileName);
        if (!System.IO.File.Exists(filePath)) return NotFound();

        // MIME 타입 추론 후 Content-Disposition: attachment 로 다운로드 강제
        string mime = "application/octet-stream";
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        mime = ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".zip" => "application/zip",
            ".txt" => "text/plain",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };

        var fileStream = System.IO.File.OpenRead(filePath);
        return File(fileStream, mime, fileName);
    }

    /// <summary>게시글 삭제 (POST)</summary>
    public IActionResult OnPostDelete(int no)
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return RedirectToPage("Community");

        using var conn = _db.OpenConnection();
        // 관련 데이터 순차 삭제 후 게시글 삭제
        // 관련 데이터 순차 삭제 (FK 순서: notifications → bbscomment → bbslike → bbs)
        conn.Execute("DELETE FROM notifications WHERE bbsno=@no", new { no });
        conn.Execute("DELETE FROM bbscomment WHERE bbsno=@no", new { no });
        conn.Execute("DELETE FROM bbslike WHERE bbsno=@no", new { no });
        conn.Execute("DELETE FROM bbs WHERE no=@no AND author=@uid", new { no, uid });

        TempData["ToastMsg"] = "게시글이 삭제되었습니다.";
        TempData["ToastType"] = "success";
        return RedirectToPage("Community");
    }

    // 게시글 데이터 DTO
    public class PostData
    {
        public string Title { get; set; } = "";
        public string Contents { get; set; } = "";
        public string AuthorNick { get; set; } = "";
        public string Author { get; set; } = "";
        public string Date { get; set; } = "";
        public int Hits { get; set; }
        public string FileName { get; set; } = "";
        public int FileSize { get; set; }
    }

    // 댓글 데이터 DTO
    public class CommentData
    {
        public int CommentId { get; set; }
        public string AuthorNick { get; set; } = "";
        public string Avatar { get; set; } = "?";
        public string Content { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public bool CanDelete { get; set; }
    }
}
