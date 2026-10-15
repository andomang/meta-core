using Dapper;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MetaSearch.Pages;

/// <summary>커뮤니티 게시글 목록 — ROW_NUMBER 서버 페이징</summary>
public class CommunityModel : PageModel
{
    private readonly Services.DbService _db;
    public readonly Services.Lang Lang;
    private const int PageSize = 10;

    public CommunityModel(Services.DbService db, Services.Lang lang)
    {
        _db = db;
        Lang = lang;
    }

    // 뷰 데이터
    public List<PostItem> Posts { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public string Keyword { get; set; } = "";

    /// <summary>게시글 목록 조회 (GET)</summary>
    public void OnGet(int page = 1, string keyword = "")
    {
        CurrentPage = page < 1 ? 1 : page;
        Keyword = keyword?.Trim() ?? "";

        using var conn = _db.OpenConnection();

        // WHERE 조건 (키워드 검색 시)
        string where = "";
        object param;
        if (!string.IsNullOrEmpty(Keyword))
        {
            where = "WHERE (b.Title LIKE @kw OR b.Contents LIKE @kw)";
            param = new { kw = "%" + Keyword + "%", limit = PageSize, offset = (CurrentPage - 1) * PageSize };
        }
        else
        {
            param = new { limit = PageSize, offset = (CurrentPage - 1) * PageSize };
        }

        // 전체 건수 조회 — PostgreSQL COUNT(*)는 bigint 반환, Convert.ToInt32으로 안전 변환
        long totalCountRaw = conn.ExecuteScalar<long>(
            $"SELECT COUNT(*) FROM bbs b {where}", param);
        int totalCount = Convert.ToInt32(totalCountRaw);
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));

        // 현재 페이지 게시글 목록 + 좋아요/댓글 수 서브쿼리
        string sql = $@"
            SELECT b.No, b.Title, b.Author, b.UploadTime, b.Hits,
                   m.nickname AS authornick,
                   COALESCE((SELECT COUNT(*) FROM bbslike l WHERE l.bbsno = b.no), 0) AS likecount,
                   COALESCE((SELECT COUNT(*) FROM bbscomment c WHERE c.bbsno = b.no), 0) AS commentcount
            FROM bbs b
            INNER JOIN members m ON b.author = m.userid
            {where}
            ORDER BY b.no DESC
            LIMIT @limit OFFSET @offset";

        var rows = conn.Query(sql, param);
        foreach (var r in rows)
        {
            // PostgreSQL은 컬럼명을 소문자로 반환 → 모두 소문자로 접근
            Posts.Add(new PostItem
            {
                No = Convert.ToInt32(r.no ?? 0),
                Title = r.title ?? "",
                AuthorNick = r.authornick?.Trim() ?? "",
                Hits = Convert.ToInt32(r.hits ?? 0),
                Date = r.uploadtime != null ? ((DateTime)r.uploadtime).ToString("yyyy.MM.dd") : "",
                LikeCount = Convert.ToInt32(r.likecount ?? 0),
                CommentCount = Convert.ToInt32(r.commentcount ?? 0)
            });
        }
    }

    /// <summary>게시글 목록 항목 DTO</summary>
    public class PostItem
    {
        public int No { get; set; }
        public string Title { get; set; } = "";
        public string AuthorNick { get; set; } = "";
        public int Hits { get; set; }
        public string Date { get; set; } = "";
        public int LikeCount { get; set; }
        public int CommentCount { get; set; }
    }
}
