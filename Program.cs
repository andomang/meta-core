// ASP.NET Core 8 Razor Pages 앱 진입점
// MSSQL → PostgreSQL + Dapper, 쿠키 인증 + 세션 기반

using MetaSearch.Services;

var builder = WebApplication.CreateBuilder(args);

// ── 서비스 등록 ──────────────────────────────────────────────────────────
// Razor Pages
builder.Services.AddRazorPages();

// 세션 (Web Forms Session 대체)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opt =>
{
    opt.IdleTimeout = TimeSpan.FromMinutes(60);
    opt.Cookie.HttpOnly = true;
    opt.Cookie.IsEssential = true;
});

// HttpContextAccessor (Lang 등에서 세션 접근용)
builder.Services.AddHttpContextAccessor();

// DB 연결 팩토리 (Dapper + SqlClient)
builder.Services.AddSingleton<DbService>();

// DAO 서비스
builder.Services.AddScoped<MemberDao>();
builder.Services.AddScoped<SearchDao>();

// HTTP 크롤러·API 서비스
builder.Services.AddHttpClient();
builder.Services.AddSingleton<GoogleCrawler>();
builder.Services.AddSingleton<NaverCrawler>();
builder.Services.AddSingleton<DaumCrawler>();
builder.Services.AddSingleton<BraveCrawler>();
builder.Services.AddSingleton<ClaudeApi>();
builder.Services.AddSingleton<ExtraApi>();

// 다국어 텍스트 서비스
builder.Services.AddSingleton<Lang>();

// ── 앱 빌드 ──────────────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();

// 세션 미들웨어 (UseRouting 뒤, MapRazorPages 앞)
app.UseSession();

app.UseAuthorization();
app.MapRazorPages();

// ── 미니멀 API 엔드포인트 (알림·검색 AJAX 호출용) ──────────────────────────

// 알림 API (레이아웃 JS에서 fetch 호출)
app.MapGet("/api/notify", (HttpContext ctx, DbService db) =>
{
    string action = ctx.Request.Query["action"].ToString();
    string? uid = ctx.Session.GetString("UserID");
    if (uid == null) return Results.Json(Array.Empty<object>());

    using var conn = db.OpenConnection();

    if (action == "getList")
    {
        // 최근 알림 20건 — PK 컬럼명은 'id' (notifid 아님), AS notifid로 별칭
        var list = Dapper.SqlMapper.Query(conn,
            @"SELECT n.id AS notifid, n.bbsno, n.type, n.isread, n.actorid,
                     b.title AS bbstitle, m.nickname AS actornick
              FROM notifications n
              LEFT JOIN bbs b ON n.bbsno = b.no
              LEFT JOIN members m ON n.actorid = m.userid
              WHERE n.userid = @uid
              ORDER BY n.createdat DESC
              LIMIT 20", new { uid });

        var items = new List<object>();
        foreach (var r in list)
        {
            // PostgreSQL은 컬럼명을 소문자로 반환, 별칭 notifid로 접근
            items.Add(new
            {
                notifID  = Convert.ToInt32(r.notifid ?? 0),
                bbsNo    = Convert.ToInt32(r.bbsno ?? 0),
                type     = ((string)(r.type ?? "comment")).Trim(),
                isRead   = r.isread is bool b ? b : false,
                actorNick = ((string)(r.actornick ?? "")).Trim(),
                bbsTitle  = (string)(r.bbstitle ?? "")
            });
        }
        return Results.Json(items);
    }

    if (action == "markRead")
    {
        // PostgreSQL 불리언은 true/false (MSSQL의 1/0 불가)
        Dapper.SqlMapper.Execute(conn, "UPDATE notifications SET isread=true WHERE userid=@uid", new { uid });
        return Results.Json(new { result = "ok" });
    }

    if (action == "markOne")
    {
        string nid = ctx.Request.Query["notifID"].ToString();
        if (int.TryParse(nid, out int notifID))
            // PK 컬럼명은 id
            Dapper.SqlMapper.Execute(conn, "UPDATE notifications SET isread=true WHERE id=@notifID AND userid=@uid", new { notifID, uid });
        return Results.Json(new { result = "ok" });
    }

    if (action == "delete")
    {
        string nid = ctx.Request.Query["notifID"].ToString();
        if (int.TryParse(nid, out int notifID))
            // PK 컬럼명은 id
            Dapper.SqlMapper.Execute(conn, "DELETE FROM notifications WHERE id=@notifID AND userid=@uid", new { notifID, uid });
        return Results.Json(new { result = "ok" });
    }

    return Results.Json(new { result = "unknown" });
});

// 검색 API (Index/SearchResults JS에서 fetch 호출)
app.MapGet("/api/search", (HttpContext ctx, SearchDao searchDao, DbService db) =>
{
    string action = ctx.Request.Query["action"].ToString();
    string? uid = ctx.Session.GetString("UserID");

    if (action == "history")
    {
        if (uid == null) return Results.Json(Array.Empty<object>());
        var items = searchDao.GetRecentSearches(uid, 10)
            .Select(r => new { id = (int)r.id, query = (string)r.query });
        return Results.Json(items);
    }

    if (action == "deleteHistory")
    {
        if (uid == null) return Results.Json(new { result = "ok" });
        string idStr = ctx.Request.Query["id"].ToString();
        if (int.TryParse(idStr, out int id))
        {
            using var conn = db.OpenConnection();
            // PostgreSQL 소문자 테이블·컬럼명
            Dapper.SqlMapper.Execute(conn, "DELETE FROM searchhistory WHERE no=@id AND userid=@uid", new { id, uid });
        }
        return Results.Json(new { result = "ok" });
    }

    return Results.Json(new { result = "unknown" });
});

// Kestrel 포트 설정 (Nginx 리버스 프록시 대상)
app.Run("http://0.0.0.0:5000");
