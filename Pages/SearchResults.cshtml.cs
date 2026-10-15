using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MetaSearch.Models;

namespace MetaSearch.Pages;

/// <summary>
/// 검색 결과 페이지 — 3개 엔진 병렬 크롤링 + RRF 결합 + AI 분류·재정렬·관련검색어.
/// 클라이언트 JS에서 batch 2+ 추가 로드, 이미지 enrichment, extraPanel 렌더링 수행.
/// </summary>
public class SearchResultsModel : PageModel
{
    private readonly Services.DbService _db;
    private readonly Services.SearchDao _searchDao;
    private readonly Services.MemberDao _memberDao;
    private readonly Services.GoogleCrawler _google;
    private readonly Services.NaverCrawler _naver;
    private readonly Services.DaumCrawler _daum;
    private readonly Services.BraveCrawler _brave;
    private readonly Services.ClaudeApi _ai;
    private readonly Services.ExtraApi _extra;
    public readonly Services.Lang Lang;

    public SearchResultsModel(
        Services.DbService db, Services.SearchDao searchDao, Services.MemberDao memberDao,
        Services.GoogleCrawler google, Services.NaverCrawler naver, Services.DaumCrawler daum,
        Services.BraveCrawler brave,
        Services.ClaudeApi ai, Services.ExtraApi extra, Services.Lang lang)
    {
        _db = db; _searchDao = searchDao; _memberDao = memberDao;
        _google = google; _naver = naver; _daum = daum; _brave = brave;
        _ai = ai; _extra = extra; Lang = lang;
    }

    // 뷰 바인딩 데이터 (JSON으로 클라이언트에 전달)
    public string Query { get; set; } = "";
    public string Keyword { get; set; } = "일반";
    public string ResultsJson { get; set; } = "[]";
    public string GoogleJson { get; set; } = "[]";
    public string NaverJson { get; set; } = "[]";
    public string DaumJson { get; set; } = "[]";
    public string BraveJson { get; set; } = "[]";
    public string ScoresJson { get; set; } = "{}";
    public string ExtraJson { get; set; } = "{}";
    public string RelatedJson { get; set; } = "[]";
    public string RankedJson { get; set; } = "[]";
    public string RankedOrderStr { get; set; } = "";
    // 사이드바 인기 검색어
    public IEnumerable<dynamic> PopularSearches { get; set; } = Enumerable.Empty<dynamic>();

    /// <summary>검색 실행 (GET ?q=검색어)</summary>
    public async Task<IActionResult> OnGetAsync(string q, string? handler = null)
    {
        // AJAX 핸들러 분기
        if (handler == "Batch") return await OnGetBatchAsync(q);
        if (handler == "History") return OnGetHistory();
        if (handler == "DeleteHistory") return OnGetDeleteHistory();
        if (handler == "Suggest") return OnGetSuggest(q);

        if (string.IsNullOrWhiteSpace(q)) return Redirect("/");
        Query = q.Trim();

        string? uid = HttpContext.Session.GetString("UserID");

        // ── 1단계: AI 분류 + 4엔진 병렬 크롤링 ──
        var classifyTask = _ai.ClassifyAsync(Query);
        var googleTask = _google.SearchAsync(Query);
        var naverTask = _naver.SearchAsync(Query);
        var daumTask = _daum.SearchAsync(Query);
        var braveTask = _brave.SearchAsync(Query);  // Brave: API키 미설정 시 빈 목록 반환

        await Task.WhenAll(classifyTask, googleTask, naverTask, daumTask, braveTask);

        Keyword = classifyTask.Result;
        var googleResults = googleTask.Result;
        var naverResults = naverTask.Result;
        var daumResults = daumTask.Result;
        var braveResults = braveTask.Result;

        // ── 2단계: 사용자별 엔진 가중치 적용 RRF ──
        // 한국어 쿼리 여부 감지 (AC00~D7A3 = 완성형 한글)
        bool isKorean = System.Text.RegularExpressions.Regex.IsMatch(Query, @"[가-힣]");

        // 기본 가중치: 모든 엔진 동일 출발 → 사용자 DB 점수 + 키워드 점수가 개인화 보정
        double gWeight = 1.0;
        double nWeight = 1.0;
        double dWeight = 1.0;
        double bWeight = braveResults.Count > 0 ? 1.0 : 0.0;

        if (uid != null)
        {
            var (gs, ns, ds) = _memberDao.GetEngineScores(uid);

            // 사용자 DB 점수 + SearchScoreTable 키워드 점수 합산
            var keywordScores = Services.SearchScoreTable.GetScores(Keyword);
            gs += keywordScores[0]; ns += keywordScores[1]; ds += keywordScores[2];

            double total = gs + ns + ds;
            if (total > 0)
            {
                // 개인화 가중치를 기본 가중치에 곱해서 적용 (0~3 범위 유지)
                gWeight = gWeight * (gs / total * 3);
                nWeight = nWeight * (ns / total * 3);
                dWeight = dWeight * (ds / total * 3);

                // 최솟값 보정 (한 엔진이 0점이 되지 않도록)
                gWeight = Math.Max(gWeight, 0.3);
                nWeight = Math.Max(nWeight, 0.3);
                dWeight = Math.Max(dWeight, 0.3);
            }
        }

        // RRF 점수 계산 (k=60)
        const int K = 60;
        var scoreMap = new Dictionary<string, (SearchResult item, double score)>();

        void AddRrf(List<SearchResult> list, double weight)
        {
            for (int i = 0; i < list.Count; i++)
            {
                double s = weight / (K + i + 1);
                string key = list[i].Url?.ToLower() ?? "";
                if (scoreMap.ContainsKey(key))
                {
                    var (item, prev) = scoreMap[key];
                    scoreMap[key] = (item, prev + s);
                }
                else
                {
                    scoreMap[key] = (list[i], s);
                }
            }
        }

        AddRrf(googleResults, gWeight);
        AddRrf(naverResults, nWeight);
        AddRrf(daumResults, dWeight);
        if (bWeight > 0) AddRrf(braveResults, bWeight);

        var rrfResults = scoreMap.Values
            .OrderByDescending(x => x.score)
            .Select(x => { x.item.Score = x.score; return x.item; })
            .ToList();

        // ── 3단계: AI 재정렬 + 관련검색어 + Extra API 병렬 호출 ──
        // 재정렬 대상: 상위 10개 결과
        var topForRank = rrfResults.Take(10).ToList();
        string rankInput = string.Join("\n", topForRank.Select((r, i) => $"{i}. {r.Title} - {r.Description?[..Math.Min(r.Description.Length, 80)]}"));

        var rankTask = _ai.RankResultsAsync(Query, rankInput);
        var relatedTask = _ai.GetRelatedAsync(Query);
        var extraTask = _extra.GetExtraAsync(Query, Keyword);

        await Task.WhenAll(rankTask, relatedTask, extraTask);

        RankedOrderStr = rankTask.Result;
        RelatedJson = relatedTask.Result;
        ExtraJson = extraTask.Result;

        // AI 재정렬 적용
        var rankedList = new List<SearchResult>();
        if (!string.IsNullOrEmpty(RankedOrderStr))
        {
            foreach (string idx in RankedOrderStr.Split(','))
            {
                if (int.TryParse(idx.Trim(), out int i) && i >= 0 && i < topForRank.Count)
                    rankedList.Add(topForRank[i]);
            }
        }

        // ── 4단계: 검색 기록 저장 + 엔진 점수 업데이트 ──
        if (uid != null)
        {
            _searchDao.AddSearchHistory(uid, Query, Keyword);

            // 키워드별 엔진 점수 델타
            var delta = Services.SearchScoreTable.GetScores(Keyword);
            _memberDao.ApplyEngineScore(uid, delta[0], delta[1], delta[2]);
        }

        // ── 5단계: 사이드바 인기 검색어 ──
        try { PopularSearches = _searchDao.GetTopSearches(10); } catch { }

        // ── 6단계: JSON 직렬화 (클라이언트 JS로 전달) ──
        var jsonOpt = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        ResultsJson = JsonSerializer.Serialize(rrfResults, jsonOpt);
        GoogleJson = JsonSerializer.Serialize(googleResults, jsonOpt);
        NaverJson = JsonSerializer.Serialize(naverResults, jsonOpt);
        DaumJson = JsonSerializer.Serialize(daumResults, jsonOpt);
        BraveJson = JsonSerializer.Serialize(braveResults, jsonOpt);
        RankedJson = JsonSerializer.Serialize(rankedList, jsonOpt);

        ScoresJson = JsonSerializer.Serialize(new
        {
            google = gWeight,
            naver = nWeight,
            daum = dWeight,
            brave = bWeight
        });

        return Page();
    }

    /// <summary>배치 추가 결과 (AJAX) — batch 번호에 따라 다른 오프셋 요청</summary>
    /// <remarks>
    /// batch=2: Google p2, Naver 11~20, Daum p2
    /// batch=3: Google p3, Naver 21~30, Daum p3
    /// batch=4: Google p4, Naver 31~40, Daum p4
    /// </remarks>
    private async Task<IActionResult> OnGetBatchAsync(string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return new JsonResult(new { google = Array.Empty<SearchResult>(), naver = Array.Empty<SearchResult>(), daum = Array.Empty<SearchResult>(), brave = Array.Empty<SearchResult>() });

        // 배치 번호 (기본 2 = 2페이지)
        int batchNum = int.TryParse(Request.Query["batch"].ToString(), out int b) && b >= 2 && b <= 5 ? b : 2;

        int naverStart = (batchNum - 1) * 10 + 1;    // 2→11, 3→21, 4→31
        int braveOffset = (batchNum - 1) * 10;        // Brave: 0-based offset

        var g = _google.SearchAsync(q, 10, batchNum);
        var n = _naver.SearchAsync(q, 10, naverStart);
        var d = _daum.SearchAsync(q, 10, batchNum);
        var bv = _brave.SearchAsync(q, 10, braveOffset);
        await Task.WhenAll(g, n, d, bv);
        return new JsonResult(new { google = g.Result, naver = n.Result, daum = d.Result, brave = bv.Result });
    }

    /// <summary>검색 기록 조회 (AJAX)</summary>
    private IActionResult OnGetHistory()
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return new JsonResult(Array.Empty<object>());
        var items = _searchDao.GetRecentSearches(uid, 10)
            .Select(r => new { id = Convert.ToInt32(r.id ?? 0), query = (string)r.query });
        return new JsonResult(items);
    }

    /// <summary>검색 기록 삭제 (AJAX)</summary>
    private IActionResult OnGetDeleteHistory()
    {
        string? uid = HttpContext.Session.GetString("UserID");
        if (uid == null) return new JsonResult(new { result = "ok" });
        string id = Request.Query["id"].ToString();
        if (!string.IsNullOrEmpty(id))
        {
            using var conn = _db.OpenConnection();
            // PK 컬럼명은 no (id 아님) — GetRecentSearches에서 MAX(no) AS id로 반환
            conn.Execute("DELETE FROM searchhistory WHERE no=@no AND userid=@uid", new { no = int.Parse(id), uid });
        }
        return new JsonResult(new { result = "ok" });
    }

    /// <summary>자동완성 제안 (AJAX)</summary>
    private IActionResult OnGetSuggest(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return new JsonResult(Array.Empty<string>());
        var items = _searchDao.GetSuggestions(q.Trim());
        return new JsonResult(items);
    }
}
