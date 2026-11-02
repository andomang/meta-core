// ASP.NET Core 8 Razor Pages 앱 진입점
// MSSQL → PostgreSQL + Dapper, 쿠키 인증 + 세션 기반

using MetaSearch.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// ── 서비스 등록 ──────────────────────────────────────────────────────────
// Razor Pages
builder.Services.AddRazorPages();

// 세션 (Web Forms Session 대체) — 쿠키 보안 강화
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opt =>
{
    opt.IdleTimeout = TimeSpan.FromMinutes(60);
    opt.Cookie.HttpOnly = true;           // JS에서 세션 쿠키 접근 차단
    opt.Cookie.IsEssential = true;
    opt.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // HTTPS에서만 전송
    opt.Cookie.SameSite = SameSiteMode.Lax;               // CSRF 방지
});

// HttpContextAccessor (Lang 등에서 세션 접근용)
builder.Services.AddHttpContextAccessor();

// DB 연결 팩토리 (Dapper + SqlClient)
builder.Services.AddSingleton<DbService>();

// DAO 서비스
builder.Services.AddScoped<MemberDao>();
builder.Services.AddScoped<SearchDao>();

// API 호출 수 카운팅 (Serper·Brave 무료 티어 한도 관리)
builder.Services.AddSingleton<ApiUsageService>();

// HTTP 크롤러·API 서비스
builder.Services.AddHttpClient();
builder.Services.AddSingleton<GoogleCrawler>();
builder.Services.AddSingleton<NaverCrawler>();
builder.Services.AddSingleton<DaumCrawler>();
builder.Services.AddSingleton<BraveCrawler>();
builder.Services.AddSingleton<ClaudeApi>();
builder.Services.AddSingleton<ExtraApi>();

// 실시간 접속 IP 추적
builder.Services.AddSingleton<ConnectionTracker>();

// 이메일 인증 서비스 (SMTP)
builder.Services.AddSingleton<EmailService>();

// 다국어 텍스트 서비스
builder.Services.AddSingleton<Lang>();

// ── 앱 빌드 ──────────────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// Nginx 리버스 프록시 뒤에서 실제 클라이언트 IP 읽기
var fwdOpts = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
fwdOpts.KnownNetworks.Clear();
fwdOpts.KnownProxies.Clear();
app.UseForwardedHeaders(fwdOpts);

app.UseStaticFiles();

// 보안 헤더 + 캐시 제어 미들웨어
app.Use(async (ctx, next) =>
{
    // ── 보안 헤더 (모든 응답에 적용) ──
    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";          // 클릭재킹 방지
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";      // MIME 스니핑 방지
    ctx.Response.Headers["X-XSS-Protection"] = "1; mode=block";     // 브라우저 XSS 필터
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    // HSTS — Nginx에서도 설정하지만 앱 레벨에서도 이중 적용
    ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    // CSP — 인라인 스크립트 허용 (Razor Pages 특성상 필요)
    // CSP 보안 헤더 — 외부 CDN(Tailwind, Lucide 등) 허용 포함
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.tailwindcss.com https://unpkg.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self' https://cdn.tailwindcss.com; " +
        "frame-ancestors 'self'";

    // ── HTML 페이지 캐시 방지 (로그아웃 후 뒤로가기 시 로그인 상태 잔존 방지) ──
    if (!ctx.Request.Path.StartsWithSegments("/lib") &&
        !ctx.Request.Path.StartsWithSegments("/css") &&
        !ctx.Request.Path.StartsWithSegments("/js") &&
        !ctx.Request.Path.StartsWithSegments("/images"))
    {
        ctx.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        ctx.Response.Headers["Pragma"] = "no-cache";
    }
    await next();
});

app.UseRouting();

// 세션 미들웨어 (UseRouting 뒤, MapRazorPages 앞)
app.UseSession();

app.UseAuthorization();

// 접속 IP 추적 미들웨어
app.Use(async (ctx, next) =>
{
    var tracker = ctx.RequestServices.GetRequiredService<ConnectionTracker>();
    string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
    if (!string.IsNullOrEmpty(ip)) tracker.Track(ip);
    await next();
});

app.MapRazorPages();

// ── 레이트 리미팅 (IP 기반 요청 제한) ────────────────────────────────────────
var _rateLimits = new System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime ResetAt)>();

// 레이트 리미트 체크 헬퍼: 제한 초과 시 true 반환
bool IsRateLimited(string key, int maxRequests, int windowSeconds)
{
    var now = DateTime.UtcNow;
    var entry = _rateLimits.GetOrAdd(key, _ => (0, now.AddSeconds(windowSeconds)));
    if (now > entry.ResetAt)
    {
        _rateLimits[key] = (1, now.AddSeconds(windowSeconds));
        return false;
    }
    if (entry.Count >= maxRequests) return true;
    _rateLimits[key] = (entry.Count + 1, entry.ResetAt);
    return false;
}

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

// 관리자 권한 확인 헬퍼 (구성 파일에서 관리자 목록 로드)
var adminUsers = new HashSet<string>(
    (builder.Configuration.GetSection("AdminUsers").Get<string[]>() ?? new[] { "test" })
    .Select(u => u.Trim().ToLowerInvariant()));
bool IsAdmin(string? uid) => uid != null && adminUsers.Contains(uid.ToLowerInvariant());

// 관리자 API 사용량 확인
app.MapGet("/api/admin/usage", (HttpContext ctx, ApiUsageService usage) =>
{
    string? uid = ctx.Session.GetString("UserID");
    if (!IsAdmin(uid)) return Results.Json(new { error = "unauthorized" });

    var summary = usage.GetAllUsageSummary();
    var result = new List<object>();
    foreach (var row in summary)
    {
        result.Add(new
        {
            engine = (string)row.engine,
            today = Convert.ToInt32(row.today_count),
            monthly = Convert.ToInt32(row.monthly_count),
            total = Convert.ToInt32(row.total_count)
        });
    }

    return Results.Json(new
    {
        limits = new
        {
            serper = "2,500 total (one-time)",
            brave = "2,000/month",
            naver = "25,000/day",
            kakao = "300/day"
        },
        usage = result
    });
});

// ── 별점 평가 API ─────────────────────────────────────────────────────────

// 평가 여부 확인 (로그인 사용자)
app.MapGet("/api/rating/check", (HttpContext ctx, DbService db) =>
{
    string? uid = ctx.Session.GetString("UserID");
    if (uid == null) return Results.Json(new { rated = false });

    using var conn = db.OpenConnection();
    var exists = Dapper.SqlMapper.QueryFirstOrDefault<int?>(conn,
        "SELECT rating FROM user_ratings WHERE userid = @uid LIMIT 1", new { uid });
    return Results.Json(new { rated = exists != null });
});

// 별점 제출 (로그인 필요, 계정당 1회)
app.MapPost("/api/rating", async (HttpContext ctx, DbService db) =>
{
    string? uid = ctx.Session.GetString("UserID");
    if (uid == null) return Results.Json(new { result = "unauthorized" });

    // 중복 평가 방지
    using var conn = db.OpenConnection();
    var already = Dapper.SqlMapper.QueryFirstOrDefault<int?>(conn,
        "SELECT id FROM user_ratings WHERE userid = @uid LIMIT 1", new { uid });
    if (already != null) return Results.Json(new { result = "already_rated" });

    // JSON 본문 파싱 (JsonElement → 명시적 변환)
    var body = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    int rating = body.TryGetProperty("rating", out var rVal) ? rVal.GetInt32() : 0;
    string comment = body.TryGetProperty("comment", out var cVal) ? cVal.GetString() ?? "" : "";

    if (rating < 1 || rating > 5) return Results.Json(new { result = "invalid_rating" });

    // XSS 방지: HTML 태그 제거 + 길이 제한 (500자)
    comment = System.Text.RegularExpressions.Regex.Replace(comment.Trim(), "<.*?>", "");
    if (comment.Length > 500) comment = comment[..500];

    Dapper.SqlMapper.Execute(conn,
        "INSERT INTO user_ratings (userid, rating, comment) VALUES (@uid, @rating, @comment)",
        new { uid, rating, comment });

    return Results.Json(new { result = "ok" });
});

// 공개 평가 목록 (비로그인 포함, 최근 20건 + 평균)
app.MapGet("/api/ratings/public", (DbService db) =>
{
    using var conn = db.OpenConnection();

    // 전체 통계
    var stats = Dapper.SqlMapper.QueryFirstOrDefault(conn,
        "SELECT COUNT(*) AS cnt, COALESCE(AVG(rating::DECIMAL), 0) AS avg_rating FROM user_ratings");

    // 최근 한줄평 있는 평가 20건
    var list = Dapper.SqlMapper.Query(conn,
        @"SELECT r.rating, r.comment, r.created_at,
                 COALESCE(m.nickname, '익명') AS nickname
          FROM user_ratings r
          LEFT JOIN members m ON r.userid = m.userid
          WHERE r.comment IS NOT NULL AND TRIM(r.comment) != ''
          ORDER BY r.created_at DESC
          LIMIT 20");

    var items = new List<object>();
    foreach (var r in list)
    {
        items.Add(new
        {
            nickname = (string)(r.nickname ?? "익명"),
            rating = Convert.ToInt32(r.rating),
            comment = (string)(r.comment ?? ""),
            date = ((DateTime)r.created_at).ToString("yyyy-MM-dd")
        });
    }

    return Results.Json(new
    {
        totalCount = Convert.ToInt32(stats?.cnt ?? 0),
        avgRating = Math.Round(Convert.ToDecimal(stats?.avg_rating ?? 0), 1),
        reviews = items
    });
});

// 관리자 별점 조회
app.MapGet("/api/admin/ratings", (HttpContext ctx, DbService db) =>
{
    string? uid = ctx.Session.GetString("UserID");
    if (!IsAdmin(uid)) return Results.Json(new { error = "unauthorized" });

    using var conn = db.OpenConnection();

    // 전체 통계
    var stats = Dapper.SqlMapper.QueryFirstOrDefault(conn,
        @"SELECT COUNT(*) AS cnt, COALESCE(AVG(rating::DECIMAL), 0) AS avg_rating
          FROM user_ratings");

    // 최근 평가 50건
    var list = Dapper.SqlMapper.Query(conn,
        @"SELECT r.id, r.userid, r.rating, r.comment, r.created_at,
                 COALESCE(m.nickname, r.userid) AS nickname
          FROM user_ratings r
          LEFT JOIN members m ON r.userid = m.userid
          ORDER BY r.created_at DESC
          LIMIT 50");

    var items = new List<object>();
    foreach (var r in list)
    {
        items.Add(new
        {
            id = Convert.ToInt32(r.id),
            userid = (string)(r.userid ?? ""),
            nickname = (string)(r.nickname ?? ""),
            rating = Convert.ToInt32(r.rating),
            comment = (string)(r.comment ?? ""),
            created_at = ((DateTime)r.created_at).ToString("yyyy-MM-dd HH:mm")
        });
    }

    return Results.Json(new
    {
        totalCount = Convert.ToInt32(stats?.cnt ?? 0),
        avgRating = Math.Round(Convert.ToDecimal(stats?.avg_rating ?? 0), 1),
        ratings = items
    });
});

// 관리자 회원 목록
app.MapGet("/api/admin/members", (HttpContext ctx, DbService db) =>
{
    string? uid = ctx.Session.GetString("UserID");
    if (!IsAdmin(uid)) return Results.Json(new { error = "unauthorized" });

    using var conn = db.OpenConnection();
    var list = Dapper.SqlMapper.Query(conn,
        @"SELECT userid, nickname, name, email, status,
                 COALESCE(last_ip, '') AS last_ip,
                 COALESCE(created_at, NOW()) AS created_at,
                 COALESCE(last_login, NOW()) AS last_login
          FROM members ORDER BY created_at DESC");

    // IP 앞 두 자리만 노출 (개인정보 보호)
    string maskIp(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return "-";
        var parts = ip.Split('.');
        if (parts.Length >= 2) return parts[0] + "." + parts[1] + ".*.*";
        return ip;
    }

    var items = new List<object>();
    foreach (var r in list)
    {
        items.Add(new
        {
            userid = (string)(r.userid ?? ""),
            nickname = (string)(r.nickname ?? ""),
            name = (string)(r.name ?? ""),
            email = (string)(r.email ?? ""),
            status = ((string)(r.status ?? "")).Trim(),
            ip = maskIp(((string)(r.last_ip ?? "")).Trim()),
            created_at = ((DateTime)r.created_at).ToString("yyyy-MM-dd"),
            last_login = ((DateTime)r.last_login).ToString("yyyy-MM-dd HH:mm")
        });
    }

    return Results.Json(new { totalMembers = items.Count, members = items });
});

// 관리자 실시간 접속 현황
app.MapGet("/api/admin/connections", (HttpContext ctx, ConnectionTracker tracker, DbService db) =>
{
    string? uid = ctx.Session.GetString("UserID");
    if (!IsAdmin(uid)) return Results.Json(new { error = "unauthorized" });

    var active = tracker.GetActiveConnections();

    // 세션에서 IP→유저ID 매핑은 불가하므로 DB에서 last_ip로 매칭
    using var conn = db.OpenConnection();
    var members = Dapper.SqlMapper.Query(conn,
        "SELECT userid, nickname, COALESCE(last_ip, '') AS last_ip FROM members WHERE last_ip IS NOT NULL AND last_ip != ''");
    var ipToUser = new Dictionary<string, (string userid, string nickname)>();
    foreach (var m in members)
    {
        string mIp = ((string)(m.last_ip ?? "")).Trim();
        if (!string.IsNullOrEmpty(mIp))
            ipToUser[mIp] = ((string)m.userid, (string)m.nickname);
    }

    // IP 앞 두 자리만 노출
    string maskIp(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length >= 2) return parts[0] + "." + parts[1] + ".*.*";
        return ip;
    }

    var items = active.Select(c =>
    {
        bool isMember = ipToUser.ContainsKey(c.Ip);
        return new
        {
            ip = maskIp(c.Ip),
            nickname = isMember ? ipToUser[c.Ip].nickname : "비회원",
            userid = isMember ? ipToUser[c.Ip].userid : "-",
            lastSeen = c.LastSeen.ToLocalTime().ToString("HH:mm:ss")
        };
    });

    return Results.Json(new { count = active.Count, connections = items });
});

// ── 회원가입 중복확인 API ────────────────────────────────────────────────

// 아이디 중복확인
app.MapGet("/api/register/check-id", (HttpContext ctx, MemberDao dao) =>
{
    string id = ctx.Request.Query["id"].ToString().Trim().ToLowerInvariant();
    if (string.IsNullOrEmpty(id))
        return Results.Json(new { result = "empty" });

    // 영문+숫자+밑줄, 3~20자 형식 검증
    if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^[a-z0-9_]{3,20}$"))
        return Results.Json(new { result = "invalid_format" });

    bool available = dao.VerifyUserID(id);
    return Results.Json(new { result = available ? "ok" : "duplicate" });
});

// 이메일 중복확인
app.MapGet("/api/register/check-email", (HttpContext ctx, DbService db) =>
{
    string email = ctx.Request.Query["email"].ToString().Trim().ToLowerInvariant();
    if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        return Results.Json(new { result = "invalid" });

    using var conn = db.OpenConnection();
    var exists = Dapper.SqlMapper.QueryFirstOrDefault<string>(conn,
        "SELECT userid FROM members WHERE LOWER(email) = @email AND (TRIM(status) = 'true' OR TRIM(status) = '1')",
        new { email });
    return Results.Json(new { result = exists == null ? "ok" : "duplicate" });
});

// ── 이메일 인증 API ──────────────────────────────────────────────────────

// 인증코드 발송 (Brevo API) — 이메일 중복도 사전 체크 + 레이트 리미팅
app.MapPost("/api/email/send-code", async (HttpContext ctx, EmailService email, DbService db) =>
{
    // 레이트 리미팅: IP당 1분에 3회
    string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (IsRateLimited($"email:{ip}", 3, 60))
        return Results.Json(new { result = "rate_limited" });

    var body = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    string addr = body.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
    addr = addr.Trim().ToLowerInvariant();

    if (string.IsNullOrEmpty(addr) || !addr.Contains('@'))
        return Results.Json(new { result = "invalid_email" });

    // 이메일 중복 체크
    using var conn = db.OpenConnection();
    var exists = Dapper.SqlMapper.QueryFirstOrDefault<string>(conn,
        "SELECT userid FROM members WHERE LOWER(email) = @addr AND (TRIM(status) = 'true' OR TRIM(status) = '1')",
        new { addr });
    if (exists != null)
        return Results.Json(new { result = "duplicate_email" });

    bool sent = await email.SendVerificationCodeAsync(addr);
    return Results.Json(new { result = sent ? "ok" : "send_failed" });
});

// 인증코드 확인
app.MapPost("/api/email/verify-code", async (HttpContext ctx, EmailService email) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    string addr = body.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
    string code = body.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";

    bool ok = email.VerifyCode(addr.Trim(), code.Trim());
    if (ok) ctx.Session.SetString("EmailVerified", addr.Trim().ToLowerInvariant());

    return Results.Json(new { result = ok ? "ok" : "invalid" });
});

// ── 계정 찾기 API (아이디/비밀번호 찾기) ─────────────────────────────────────

// 아이디 찾기: 이메일로 인증코드 발송
app.MapPost("/api/account/find-id", async (HttpContext ctx, EmailService email, DbService db) =>
{
    // 레이트 리미팅: IP당 1분에 3회
    string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (IsRateLimited($"find-id:{ip}", 3, 60))
        return Results.Json(new { result = "rate_limited" });

    var body = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    string addr = body.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
    addr = addr.Trim().ToLowerInvariant();

    if (string.IsNullOrEmpty(addr) || !addr.Contains('@'))
        return Results.Json(new { result = "invalid_email" });

    // 해당 이메일로 가입된 계정 존재 여부 확인
    using var conn = db.OpenConnection();
    var exists = Dapper.SqlMapper.QueryFirstOrDefault<string>(conn,
        "SELECT userid FROM members WHERE LOWER(email) = @addr AND (TRIM(status) = 'true' OR TRIM(status) = '1')",
        new { addr });
    if (exists == null)
        return Results.Json(new { result = "not_found" });

    // 인증코드 발송 (기존 EmailService 재사용)
    bool sent = await email.SendVerificationCodeAsync(addr);
    return Results.Json(new { result = sent ? "ok" : "send_failed" });
});

// 아이디 찾기: 인증코드 확인 후 마스킹된 아이디 반환
app.MapPost("/api/account/find-id/verify", (HttpContext ctx, EmailService email, DbService db) =>
{
    var body = ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>().Result;
    string addr = body.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
    string code = body.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";

    bool ok = email.VerifyCode(addr.Trim(), code.Trim());
    if (!ok) return Results.Json(new { result = "invalid_code" });

    // 아이디 조회 후 마스킹 (앞 2자리 + *** + 뒤 1자리)
    using var conn = db.OpenConnection();
    string? userid = Dapper.SqlMapper.QueryFirstOrDefault<string>(conn,
        "SELECT userid FROM members WHERE LOWER(email) = @addr AND (TRIM(status) = 'true' OR TRIM(status) = '1')",
        new { addr = addr.Trim().ToLowerInvariant() });

    if (userid == null) return Results.Json(new { result = "not_found" });

    userid = userid.Trim();
    // 마스킹: 3자 이하 → 첫 글자 + **, 4자 이상 → 앞2 + *** + 끝1
    string masked = userid.Length <= 3
        ? userid[0] + new string('*', userid.Length - 1)
        : userid[..2] + new string('*', userid.Length - 3) + userid[^1..];

    return Results.Json(new { result = "ok", userid = masked });
});

// 비밀번호 찾기: 아이디+이메일 확인 후 인증코드 발송
app.MapPost("/api/account/find-pw", async (HttpContext ctx, EmailService email, DbService db) =>
{
    string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (IsRateLimited($"find-pw:{ip}", 3, 60))
        return Results.Json(new { result = "rate_limited" });

    var body = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    string userid = body.TryGetProperty("userid", out var u) ? u.GetString() ?? "" : "";
    string addr = body.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
    userid = userid.Trim().ToLowerInvariant();
    addr = addr.Trim().ToLowerInvariant();

    if (string.IsNullOrEmpty(userid) || string.IsNullOrEmpty(addr) || !addr.Contains('@'))
        return Results.Json(new { result = "invalid_input" });

    // 아이디+이메일 일치 확인
    using var conn = db.OpenConnection();
    var exists = Dapper.SqlMapper.QueryFirstOrDefault<string>(conn,
        "SELECT userid FROM members WHERE LOWER(userid) = @userid AND LOWER(email) = @addr AND (TRIM(status) = 'true' OR TRIM(status) = '1')",
        new { userid, addr });
    if (exists == null)
        return Results.Json(new { result = "not_found" });

    bool sent = await email.SendVerificationCodeAsync(addr);
    return Results.Json(new { result = sent ? "ok" : "send_failed" });
});

// 비밀번호 찾기: 인증코드 확인
app.MapPost("/api/account/find-pw/verify", (HttpContext ctx, EmailService email) =>
{
    var body = ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>().Result;
    string addr = body.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
    string code = body.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";

    bool ok = email.VerifyCode(addr.Trim(), code.Trim());
    if (!ok) return Results.Json(new { result = "invalid_code" });

    // 세션에 비밀번호 재설정 허용 플래그 저장
    ctx.Session.SetString("PwResetEmail", addr.Trim().ToLowerInvariant());
    return Results.Json(new { result = "ok" });
});

// 비밀번호 찾기: 새 비밀번호 설정
app.MapPost("/api/account/find-pw/reset", (HttpContext ctx, DbService db) =>
{
    var body = ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>().Result;
    string userid = body.TryGetProperty("userid", out var u) ? u.GetString() ?? "" : "";
    string newPw = body.TryGetProperty("newPw", out var p) ? p.GetString() ?? "" : "";

    userid = userid.Trim().ToLowerInvariant();

    // 세션에서 비밀번호 재설정 허용 여부 확인
    string? resetEmail = ctx.Session.GetString("PwResetEmail");
    if (string.IsNullOrEmpty(resetEmail))
        return Results.Json(new { result = "unauthorized" });

    // 비밀번호 정책 검증 (8자 이상, 영문+숫자+특수문자 포함)
    if (newPw.Length < 8 ||
        !System.Text.RegularExpressions.Regex.IsMatch(newPw, @"[A-Za-z]") ||
        !System.Text.RegularExpressions.Regex.IsMatch(newPw, @"[0-9]") ||
        !System.Text.RegularExpressions.Regex.IsMatch(newPw, @"[^A-Za-z0-9]"))
        return Results.Json(new { result = "weak_password" });

    // 아이디+이메일 일치 확인 후 비밀번호 재설정
    using var conn = db.OpenConnection();
    var exists = Dapper.SqlMapper.QueryFirstOrDefault<string>(conn,
        "SELECT userid FROM members WHERE LOWER(userid) = @userid AND LOWER(email) = @resetEmail AND (TRIM(status) = 'true' OR TRIM(status) = '1')",
        new { userid, resetEmail });
    if (exists == null)
        return Results.Json(new { result = "not_found" });

    // BCrypt 해싱 후 저장
    string hash = BCrypt.Net.BCrypt.HashPassword(newPw, workFactor: 11);
    Dapper.SqlMapper.Execute(conn,
        "UPDATE members SET passwd = @hash WHERE LOWER(userid) = @userid",
        new { hash, userid });

    // 세션 플래그 제거
    ctx.Session.Remove("PwResetEmail");
    return Results.Json(new { result = "ok" });
});

// ── 동적 사이트맵 (커뮤니티 게시글 포함, 검색엔진 크롤링 최적화) ──
app.MapGet("/sitemap.xml", (DbService db) =>
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
    sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

    // 고정 페이지
    var pages = new[] {
        ("/", "daily", "1.0"),
        ("/SearchResults", "always", "0.9"),
        ("/Community", "daily", "0.8"),
        ("/Eval", "weekly", "0.7"),
        ("/EvalReport", "weekly", "0.6"),
        ("/Register", "monthly", "0.5"),
        ("/Login", "monthly", "0.4"),
        ("/Privacy", "yearly", "0.2")
    };
    foreach (var (path, freq, priority) in pages)
    {
        sb.AppendLine($"  <url><loc>https://metasearch.kro.kr{path}</loc><changefreq>{freq}</changefreq><priority>{priority}</priority></url>");
    }

    // 커뮤니티 게시글 동적 추가 (최근 200개)
    try
    {
        using var conn = db.OpenConnection();
        var posts = Dapper.SqlMapper.Query<(int no, DateTime uploadtime)>(conn,
            "SELECT no, uploadtime FROM bbs ORDER BY no DESC LIMIT 200");
        foreach (var p in posts)
        {
            var lastmod = p.uploadtime.ToString("yyyy-MM-dd");
            sb.AppendLine($"  <url><loc>https://metasearch.kro.kr/CommunityView?no={p.no}</loc><lastmod>{lastmod}</lastmod><changefreq>weekly</changefreq><priority>0.5</priority></url>");
        }
    }
    catch { }

    sb.AppendLine("</urlset>");
    return Results.Content(sb.ToString(), "application/xml; charset=utf-8");
});

// ── 북마크 API (검색 결과 저장/조회/삭제) ──────────────────────────────────

// 북마크 목록 조회
app.MapGet("/api/bookmarks", (HttpContext ctx, DbService db) =>
{
    string? uid = ctx.Session.GetString("UserID");
    if (uid == null) return Results.Json(new { items = Array.Empty<object>() });

    using var conn = db.OpenConnection();
    var list = Dapper.SqlMapper.Query(conn,
        "SELECT id, title, url, description, source, folder, created_at FROM bookmarks WHERE userid = @uid ORDER BY created_at DESC LIMIT 100",
        new { uid });

    var items = new List<object>();
    foreach (var r in list)
    {
        items.Add(new
        {
            id = Convert.ToInt32(r.id),
            title = (string)(r.title ?? ""),
            url = (string)(r.url ?? ""),
            description = (string)(r.description ?? ""),
            source = (string)(r.source ?? ""),
            folder = (string)(r.folder ?? "기본"),
            created_at = ((DateTime)r.created_at).ToString("yyyy-MM-dd HH:mm")
        });
    }

    // 폴더 목록도 함께 반환
    var folders = Dapper.SqlMapper.Query<string>(conn,
        "SELECT DISTINCT folder FROM bookmarks WHERE userid = @uid ORDER BY folder", new { uid });

    return Results.Json(new { items, folders });
});

// 북마크 추가
app.MapPost("/api/bookmarks", async (HttpContext ctx, DbService db) =>
{
    string? uid = ctx.Session.GetString("UserID");
    if (uid == null) return Results.Json(new { result = "unauthorized" });

    var body = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    string title = body.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
    string url = body.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
    string desc = body.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
    string source = body.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
    string folder = body.TryGetProperty("folder", out var f) ? f.GetString() ?? "기본" : "기본";

    if (string.IsNullOrEmpty(url)) return Results.Json(new { result = "invalid" });

    // XSS 방지
    title = System.Text.RegularExpressions.Regex.Replace(title, "<.*?>", "");
    if (title.Length > 200) title = title[..200];
    if (folder.Length > 50) folder = folder[..50];

    // 중복 확인
    using var conn = db.OpenConnection();
    var exists = Dapper.SqlMapper.QueryFirstOrDefault<int?>(conn,
        "SELECT id FROM bookmarks WHERE userid = @uid AND url = @url LIMIT 1",
        new { uid, url });
    if (exists != null) return Results.Json(new { result = "duplicate" });

    Dapper.SqlMapper.Execute(conn,
        "INSERT INTO bookmarks (userid, title, url, description, source, folder) VALUES (@uid, @title, @url, @desc, @source, @folder)",
        new { uid, title, url, desc, source, folder });

    return Results.Json(new { result = "ok" });
});

// 북마크 삭제
app.MapDelete("/api/bookmarks/{id:int}", (int id, HttpContext ctx, DbService db) =>
{
    string? uid = ctx.Session.GetString("UserID");
    if (uid == null) return Results.Json(new { result = "unauthorized" });

    using var conn = db.OpenConnection();
    Dapper.SqlMapper.Execute(conn,
        "DELETE FROM bookmarks WHERE id = @id AND userid = @uid",
        new { id, uid });

    return Results.Json(new { result = "ok" });
});

// ── 인기검색어 트렌드 API (시간대별 TOP 10) ─────────────────────────────────

app.MapGet("/api/trends", (DbService db) =>
{
    using var conn = db.OpenConnection();

    // 현재 인기검색어 TOP 10 (최근 24시간)
    var top10 = Dapper.SqlMapper.Query(conn,
        @"SELECT query, COUNT(*) AS cnt
          FROM searchhistory WHERE searchtime > NOW() - INTERVAL '24 hours'
          GROUP BY query ORDER BY cnt DESC LIMIT 10");

    var items = new List<object>();
    int rank = 1;
    foreach (var r in top10)
    {
        items.Add(new { rank = rank++, query = (string)(r.query ?? ""), count = Convert.ToInt32(r.cnt) });
    }

    // 시간대별 검색량 (최근 24시간, 시간 단위 집계)
    var hourly = Dapper.SqlMapper.Query(conn,
        @"SELECT DATE_TRUNC('hour', searchtime) AS hour, COUNT(*) AS cnt
          FROM searchhistory WHERE searchtime > NOW() - INTERVAL '24 hours'
          GROUP BY DATE_TRUNC('hour', searchtime) ORDER BY hour");

    var chart = new List<object>();
    foreach (var r in hourly)
    {
        chart.Add(new { hour = ((DateTime)r.hour).ToString("HH:00"), count = Convert.ToInt32(r.cnt) });
    }

    // 총 검색 수 (오늘)
    long todayTotal = Dapper.SqlMapper.ExecuteScalar<long>(conn,
        "SELECT COUNT(*) FROM searchhistory WHERE searchtime > CURRENT_DATE");

    return Results.Json(new { top10 = items, hourly = chart, todayTotal = Convert.ToInt32(todayTotal) });
});

// ── AI 검색 요약 API ─────────────────────────────────────────────────────────

app.MapGet("/api/ai-summary", async (HttpContext ctx, ClaudeApi ai) =>
{
    string q = ctx.Request.Query["q"].ToString().Trim();
    if (string.IsNullOrEmpty(q) || q.Length > 200)
        return Results.Json(new { summary = "" });

    // 레이트 리미팅: IP당 1분에 5회
    string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (IsRateLimited($"ai-summary:{ip}", 5, 60))
        return Results.Json(new { summary = "", limited = true });

    try
    {
        string summary = await ai.SummarizeQueryAsync(q);
        return Results.Json(new { summary });
    }
    catch
    {
        return Results.Json(new { summary = "" });
    }
});

// ── 관리자 통계 API (대시보드 차트용) ────────────────────────────────────────

app.MapGet("/api/admin/stats", (HttpContext ctx, DbService db) =>
{
    string? uid = ctx.Session.GetString("UserID");
    if (!IsAdmin(uid)) return Results.Json(new { error = "unauthorized" });

    using var conn = db.OpenConnection();

    // 일별 가입자 수 (최근 30일)
    var dailySignups = Dapper.SqlMapper.Query(conn,
        @"SELECT DATE(created_at) AS day, COUNT(*) AS cnt
          FROM members WHERE created_at > NOW() - INTERVAL '30 days'
          GROUP BY DATE(created_at) ORDER BY day");

    var signups = new List<object>();
    foreach (var r in dailySignups)
        signups.Add(new { day = ((DateTime)r.day).ToString("MM-dd"), count = Convert.ToInt32(r.cnt) });

    // 일별 검색량 (최근 30일)
    var dailySearches = Dapper.SqlMapper.Query(conn,
        @"SELECT DATE(searchtime) AS day, COUNT(*) AS cnt
          FROM searchhistory WHERE searchtime > NOW() - INTERVAL '30 days'
          GROUP BY DATE(searchtime) ORDER BY day");

    var searches = new List<object>();
    foreach (var r in dailySearches)
        searches.Add(new { day = ((DateTime)r.day).ToString("MM-dd"), count = Convert.ToInt32(r.cnt) });

    // 일별 게시글 수 (최근 30일)
    var dailyPosts = Dapper.SqlMapper.Query(conn,
        @"SELECT DATE(uploadtime) AS day, COUNT(*) AS cnt
          FROM bbs WHERE uploadtime > NOW() - INTERVAL '30 days'
          GROUP BY DATE(uploadtime) ORDER BY day");

    var posts = new List<object>();
    foreach (var r in dailyPosts)
        posts.Add(new { day = ((DateTime)r.day).ToString("MM-dd"), count = Convert.ToInt32(r.cnt) });

    // 총계
    long totalMembers = Dapper.SqlMapper.ExecuteScalar<long>(conn, "SELECT COUNT(*) FROM members");
    long totalSearches = Dapper.SqlMapper.ExecuteScalar<long>(conn, "SELECT COUNT(*) FROM searchhistory");
    long totalPosts = Dapper.SqlMapper.ExecuteScalar<long>(conn, "SELECT COUNT(*) FROM bbs");
    long totalBookmarks = 0;
    try { totalBookmarks = Dapper.SqlMapper.ExecuteScalar<long>(conn, "SELECT COUNT(*) FROM bookmarks"); } catch { }

    return Results.Json(new
    {
        dailySignups = signups,
        dailySearches = searches,
        dailyPosts = posts,
        totals = new
        {
            members = Convert.ToInt32(totalMembers),
            searches = Convert.ToInt32(totalSearches),
            posts = Convert.ToInt32(totalPosts),
            bookmarks = Convert.ToInt32(totalBookmarks)
        }
    });
});

// ── Extra 패널 재호출 API (카카오맵, 쇼핑, 뉴스, 유튜브, 날씨 등) ──────────

app.MapGet("/api/search/extra", async (HttpContext ctx, ExtraApi extra) =>
{
    string q = ctx.Request.Query["q"].ToString().Trim();
    string type = ctx.Request.Query["type"].ToString().Trim();
    if (string.IsNullOrEmpty(q) || string.IsNullOrEmpty(type))
        return Results.Json(new { data = "{}" });

    // 타입별 개별 호출
    string json = type switch
    {
        "places"   => await extra.GetExtraByTypeAsync(q, "places"),
        "shopping" => await extra.GetExtraByTypeAsync(q, "shopping"),
        "news"     => await extra.GetExtraByTypeAsync(q, "news"),
        "blog"     => await extra.GetExtraByTypeAsync(q, "blog"),
        "youtube"  => await extra.GetExtraByTypeAsync(q, "youtube"),
        "wiki"     => await extra.GetExtraByTypeAsync(q, "wiki"),
        "weather"  => await extra.GetExtraByTypeAsync(q, "weather"),
        "exchange" => await extra.GetExtraByTypeAsync(q, "exchange"),
        _ => "{}"
    };

    return Results.Json(new { data = json });
});

// ── 관련 검색어 재호출 API ───────────────────────────────────────────────────

app.MapGet("/api/search/related", async (HttpContext ctx, ClaudeApi ai) =>
{
    string q = ctx.Request.Query["q"].ToString().Trim();
    if (string.IsNullOrEmpty(q)) return Results.Json(new { related = "[]" });

    string json = await ai.GetRelatedAsync(q);
    return Results.Json(new { related = json });
});

// ── 검색 자동완성 API ────────────────────────────────────────────────────────

app.MapGet("/api/autocomplete", (HttpContext ctx, DbService db) =>
{
    string q = ctx.Request.Query["q"].ToString().Trim();
    if (q.Length < 1 || q.Length > 100)
        return Results.Json(new { suggestions = Array.Empty<string>() });

    using var conn = db.OpenConnection();

    // 최근 검색어 중 일치하는 것 (빈도순 정렬)
    var rows = Dapper.SqlMapper.Query<string>(conn,
        @"SELECT query FROM (
            SELECT query, COUNT(*) AS cnt
            FROM searchhistory
            WHERE query ILIKE @pattern
            GROUP BY query ORDER BY cnt DESC LIMIT 8
          ) sub",
        new { pattern = q + "%" });

    return Results.Json(new { suggestions = rows.ToList() });
});

// ── 사용자 대시보드 API (개인 검색 통계) ─────────────────────────────────────

app.MapGet("/api/dashboard", (HttpContext ctx, DbService db) =>
{
    string? uid = ctx.Session.GetString("UserID");
    if (uid == null) return Results.Json(new { error = "login_required" });

    using var conn = db.OpenConnection();

    // 내 검색 수 (최근 30일, 일별)
    var mySearches = Dapper.SqlMapper.Query(conn,
        @"SELECT DATE(searchtime) AS day, COUNT(*) AS cnt
          FROM searchhistory WHERE userid = @uid AND searchtime > NOW() - INTERVAL '30 days'
          GROUP BY DATE(searchtime) ORDER BY day",
        new { uid });

    var searches = new List<object>();
    foreach (var r in mySearches)
        searches.Add(new { day = ((DateTime)r.day).ToString("MM-dd"), count = Convert.ToInt32(r.cnt) });

    // 내 자주 사용하는 키워드 TOP 10
    var topKeywords = Dapper.SqlMapper.Query(conn,
        @"SELECT query, COUNT(*) AS cnt
          FROM searchhistory WHERE userid = @uid
          GROUP BY query ORDER BY cnt DESC LIMIT 10",
        new { uid });

    var keywords = new List<object>();
    foreach (var r in topKeywords)
        keywords.Add(new { query = (string)(r.query ?? ""), count = Convert.ToInt32(r.cnt) });

    // 내 게시글 수
    long myPosts = Dapper.SqlMapper.ExecuteScalar<long>(conn,
        "SELECT COUNT(*) FROM bbs WHERE author = @uid", new { uid });

    // 내 북마크 수
    long myBookmarks = 0;
    try { myBookmarks = Dapper.SqlMapper.ExecuteScalar<long>(conn,
        "SELECT COUNT(*) FROM bookmarks WHERE userid = @uid", new { uid }); } catch { }

    // 총 검색 수
    long totalSearches = Dapper.SqlMapper.ExecuteScalar<long>(conn,
        "SELECT COUNT(*) FROM searchhistory WHERE userid = @uid", new { uid });

    return Results.Json(new
    {
        dailySearches = searches,
        topKeywords = keywords,
        totals = new
        {
            searches = Convert.ToInt32(totalSearches),
            posts = Convert.ToInt32(myPosts),
            bookmarks = Convert.ToInt32(myBookmarks)
        }
    });
});

// Kestrel 포트 설정 (Nginx 리버스 프록시 대상)
app.Run("http://0.0.0.0:5000");
