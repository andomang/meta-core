using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MetaSearch.Models;

namespace MetaSearch.Pages;

/// <summary>
/// 검색 품질 평가 페이지.
/// GET /Eval             → UI 페이지
/// GET /Eval?handler=Run&n=20 → JSON 평가 결과 반환
/// </summary>
public class EvalModel : PageModel
{
    private readonly Services.DbService _db;
    private readonly Services.GoogleCrawler _google;
    private readonly Services.NaverCrawler _naver;
    private readonly Services.DaumCrawler _daum;
    private readonly Services.BraveCrawler _brave;
    private readonly Services.ClaudeApi _ai;

    public EvalModel(
        Services.DbService db,
        Services.GoogleCrawler google, Services.NaverCrawler naver, Services.DaumCrawler daum,
        Services.BraveCrawler brave,
        Services.ClaudeApi ai)
    {
        _db = db; _google = google; _naver = naver; _daum = daum; _brave = brave; _ai = ai;
    }

    // async 핸들러 — 예외를 catch해서 JSON 에러로 반환 (500 HTML 대신)
    public async Task<IActionResult> OnGetAsync(string? handler, int n = 10)
    {
        if (handler == "Run")
        {
            try { return await RunEvalAsync(n); }
            catch (Exception ex)
            {
                return new JsonResult(new { error = "서버 오류: " + ex.GetType().Name + " — " + ex.Message });
            }
        }
        return Page();
    }

    // ── 평가 실행 ──
    private async Task<IActionResult> RunEvalAsync(int queryCount)
    {
        // n=20, delay=4s → 최대 ~80초 (nginx proxy_read_timeout 600s 설정됨)
        if (queryCount < 1) queryCount = 20;
        if (queryCount > 20) queryCount = 20;

        // 1. SearchHistory 인기 쿼리 + 기본 테스트셋 60개 혼합 후 랜덤 n개 선택
        var queries = BuildRandomQuerySet(queryCount);

        double sumG = 0, sumN = 0, sumD = 0, sumB = 0, sumR = 0;
        double sumMrrG = 0, sumMrrN = 0, sumMrrD = 0, sumMrrB = 0, sumMrrR = 0;
        double sumP5G = 0, sumP5N = 0, sumP5D = 0, sumP5B = 0, sumP5R = 0;
        int valid = 0;
        var byQuery = new List<object>();

        foreach (var kv in queries)
        {
            string q = kv.Key, keyword = kv.Value;
            try
            {
                // 2. 4엔진 병렬 검색
                var gTask = _google.SearchAsync(q, 10);
                var nTask = _naver.SearchAsync(q, 10);
                var dTask = _daum.SearchAsync(q, 10);
                var bTask = _brave.SearchAsync(q, 10);
                await Task.WhenAll(gTask, nTask, dTask, bTask);

                var gRes = gTask.Result;
                var nRes = nTask.Result;
                var dRes = dTask.Result;
                var bRes = bTask.Result;

                // 3. RRF 통합 (4엔진 — Brave가 빈 목록이면 3엔진과 동일)
                var rRes = RrfCombine(gRes, nRes, dRes, bRes, 10);

                // 4. URL 풀 구성 (엔진별 균등 6·6·5·5)
                var pool = BuildPool(gRes, nRes, dRes, bRes);
                if (pool.Count == 0) continue;

                string[] titles   = pool.Select(r => r.Title       ?? "").ToArray();
                string[] snippets = pool.Select(r => r.Description ?? "").ToArray();

                // 5. LLM 관련성 레이블링
                int[] rawScores = await _ai.LabelRelevanceAsync(q, titles, snippets);
                var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < pool.Count; i++)
                    labels[pool[i].Url ?? ""] = i < rawScores.Length ? rawScores[i] : 0;

                // Groq RPM 제한 대응: 30RPM = 2초/요청, 여유분 포함 4초 대기
                // 15쿼리 × 4초 = 60초 → nginx 기본 timeout 이내
                await Task.Delay(4000);

                // 6. 지표 계산
                double ndG = Ndcg(gRes, labels, 10), ndN = Ndcg(nRes, labels, 10);
                double ndD = Ndcg(dRes, labels, 10), ndB = Ndcg(bRes, labels, 10);
                double ndR = Ndcg(rRes, labels, 10);
                double mrG = Mrr(gRes, labels), mrN = Mrr(nRes, labels);
                double mrD = Mrr(dRes, labels), mrB = Mrr(bRes, labels), mrR = Mrr(rRes, labels);
                double pG = Precision(gRes, labels, 5), pN = Precision(nRes, labels, 5);
                double pD = Precision(dRes, labels, 5), pB = Precision(bRes, labels, 5);
                double pR = Precision(rRes, labels, 5);

                sumG += ndG; sumN += ndN; sumD += ndD; sumB += ndB; sumR += ndR;
                sumMrrG += mrG; sumMrrN += mrN; sumMrrD += mrD; sumMrrB += mrB; sumMrrR += mrR;
                sumP5G += pG; sumP5N += pN; sumP5D += pD; sumP5B += pB; sumP5R += pR;
                valid++;

                // 레이블링 품질 확인 — 전부 0이면 AI가 응답 실패한 것
                int labeledCount = rawScores.Count(s => s > 0);
                byQuery.Add(new {
                    query = q, keyword,
                    labeledCount,  // 0이면 AI 레이블링 실패
                    poolSize = pool.Count,
                    nDCG10 = new { Google = R(ndG), Naver = R(ndN), Daum = R(ndD), Brave = R(ndB), RRF = R(ndR) },
                    MRR    = new { Google = R(mrG), Naver = R(mrN), Daum = R(mrD), Brave = R(mrB), RRF = R(mrR) },
                    P5     = new { Google = R(pG),  Naver = R(pN),  Daum = R(pD),  Brave = R(pB),  RRF = R(pR)  }
                });
            }
            catch (Exception ex)
            {
                byQuery.Add(new { query = q, keyword, error = ex.Message });
            }
        }

        if (valid == 0)
            return new JsonResult(new { error = "유효한 평가 결과 없음" });

        // 7. 집계 결과 반환 — PropertyNamingPolicy = null로 PascalCase 유지
        // 기본 camelCase 직렬화 시 Google→google, MRR→mRR, P5→p5 등으로 바뀌어
        // JS에서 agg.nDCG10.Google 접근 시 undefined 에러 발생
        var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = null };
        return new JsonResult(new {
            queryCount = valid,
            aggregate = new {
                nDCG10 = new { Google = R(sumG/valid),    Naver = R(sumN/valid),    Daum = R(sumD/valid),    Brave = R(sumB/valid),    RRF = R(sumR/valid) },
                MRR    = new { Google = R(sumMrrG/valid), Naver = R(sumMrrN/valid), Daum = R(sumMrrD/valid), Brave = R(sumMrrB/valid), RRF = R(sumMrrR/valid) },
                P5     = new { Google = R(sumP5G/valid),  Naver = R(sumP5N/valid),  Daum = R(sumP5D/valid),  Brave = R(sumP5B/valid),  RRF = R(sumP5R/valid) }
            },
            byQuery
        }, jsonOpts);
    }

    // ── 60개 고정 테스트셋에서 랜덤 n개 선택 (매 실행마다 다른 20개) ──
    private List<KeyValuePair<string, string>> BuildRandomQuerySet(int n)
    {
        // DB 없이 고정 60개 풀에서만 선택 → 실행마다 다른 조합
        var pool = GetDefaultQueries(60);
        var rng = new Random();
        return pool.OrderBy(_ => rng.Next()).Take(n).ToList();
    }

    // ── DB: SearchHistory 인기 쿼리 ──
    private List<KeyValuePair<string, string>> GetTopQueries(int n)
    {
        var list = new List<KeyValuePair<string, string>>();
        try
        {
            using var conn = _db.OpenConnection();
            // PostgreSQL은 LIMIT 사용 (MSSQL의 TOP 불가)
            string sql = $"SELECT query, MAX(keyword) AS keyword FROM searchhistory GROUP BY query ORDER BY COUNT(*) DESC LIMIT {n}";
            var rows = conn.Query(sql);
            foreach (var row in rows)
            {
                // PostgreSQL 소문자 컬럼명으로 접근
                string q = ((string)(row.query ?? "")).Trim();
                string k = ((string)(row.keyword ?? "일반")).Trim();
                if (!string.IsNullOrEmpty(q)) list.Add(new(q, k));
            }
        }
        catch { }
        return list;
    }

    // ── 기본 테스트셋 60개 ──
    private List<KeyValuePair<string, string>> GetDefaultQueries(int max)
    {
        var all = new List<KeyValuePair<string, string>>
        {
            // 구글이 강한 글로벌·IT (12개)
            new("나이키 운동화",      "쇼핑"),
            new("아이폰16",           "쇼핑"),
            new("갤럭시 S25",         "쇼핑"),
            new("파이썬 튜토리얼",   "IT기술"),
            new("챗GPT",              "IT기술"),
            new("구글 제미나이",      "IT기술"),
            new("코스피",             "국내뉴스"),
            new("나스닥",             "해외뉴스"),
            new("테슬라 모델3",       "차량"),
            new("리그오브레전드",     "게임"),
            new("배틀그라운드",       "게임"),
            new("트럼프",             "해외뉴스"),
            // 네이버·다음이 강한 국내 생활·음식 (12개)
            new("홍대 맛집",          "국내맛집"),
            new("강남 삼겹살",        "국내맛집"),
            new("서울 근교 맛집 추천","국내맛집"),
            new("부산 맛집 추천",     "국내맛집"),
            new("제주도 맛집",        "국내맛집"),
            new("경리단길 카페",      "국내맛집"),
            new("성수동 브런치",      "국내맛집"),
            new("을지로 술집",        "국내맛집"),
            new("국내 여행지 추천",   "국내장소"),
            new("경복궁",             "국내장소"),
            new("북한산 등산코스",    "국내장소"),
            new("한강 피크닉 명소",   "국내장소"),
            // 의료·건강 (6개)
            new("독감 증상",          "의료건강"),
            new("고혈압",             "의료건강"),
            new("다이어트 식단",      "의료건강"),
            new("불면증 원인",        "의료건강"),
            new("허리디스크 증상",    "의료건강"),
            new("당뇨병 초기증상",    "의료건강"),
            // 해외여행·맛집 (8개)
            new("오사카 맛집",        "해외맛집"),
            new("도쿄 라멘",          "해외맛집"),
            new("방콕 여행 코스",     "해외장소"),
            new("베트남 다낭 여행",   "해외장소"),
            new("도쿄 여행 코스",     "해외장소"),
            new("파리 여행 준비물",   "해외장소"),
            new("뉴욕 여행 명소",     "해외장소"),
            new("바르셀로나 여행",    "해외장소"),
            // 스포츠·연예 (8개)
            new("손흥민",             "스포츠"),
            new("KBO 순위",           "스포츠"),
            new("EPL 순위",           "스포츠"),
            new("류현진",             "스포츠"),
            new("김연아",             "스포츠"),
            new("한국 드라마 추천",   "일반"),
            new("넷플릭스 신작",      "일반"),
            new("BTS 근황",           "일반"),
            // 경제·시사 (8개)
            new("한국 금리",          "국내뉴스"),
            new("부동산 전망",        "국내뉴스"),
            new("환율 전망",          "국내뉴스"),
            new("물가 상승률",        "국내뉴스"),
            new("반도체 시장 전망",   "해외뉴스"),
            new("원유 가격",          "해외뉴스"),
            // 차량 (6개)
            new("현대 아이오닉",      "차량"),
            new("기아 EV9",           "차량"),
            new("BMW 3시리즈",        "차량"),
            new("중고차 시세",        "차량"),
            new("전기차 충전소",      "차량"),
            new("자동차 보험료",      "차량"),
        };
        return all.GetRange(0, Math.Min(max, all.Count));
    }

    // ── URL 풀 구성 (엔진별 균등 6·6·5·5, Brave 없으면 7·7·6) ──
    private List<SearchResult> BuildPool(List<SearchResult> g, List<SearchResult> n, List<SearchResult> d, List<SearchResult> b)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pool = new List<SearchResult>();
        bool hasBrave = b.Count > 0;
        int[] quotas = hasBrave ? [6, 6, 5, 5] : [7, 7, 6, 0];
        var srcs = new[] { g, n, d, b };
        for (int s = 0; s < srcs.Length; s++)
        {
            int added = 0;
            foreach (var r in srcs[s])
            {
                if (string.IsNullOrEmpty(r.Url)) continue;
                if (seen.Add(r.Url)) { pool.Add(r); added++; }
                if (added >= quotas[s]) break;
            }
        }
        return pool;
    }

    // ── RRF 4엔진 통합 (Brave 빈 목록이면 3엔진과 동일) ──
    private static List<SearchResult> RrfCombine(List<SearchResult> g, List<SearchResult> n, List<SearchResult> d, List<SearchResult> b, int topN)
    {
        const int K = 60;
        var scoreMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var itemMap  = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var eng in new[] { g, n, d, b })
        {
            for (int i = 0; i < eng.Count; i++)
            {
                string url = eng[i].Url ?? "";
                if (string.IsNullOrEmpty(url)) continue;
                if (!scoreMap.ContainsKey(url)) { scoreMap[url] = 0; itemMap[url] = eng[i]; }
                scoreMap[url] += 1.0 / (K + i + 1);
            }
        }
        return scoreMap.OrderByDescending(kv => kv.Value)
            .Take(topN).Select(kv => itemMap[kv.Key]).ToList();
    }

    // ── 지표 계산 ──
    private static double Ndcg(List<SearchResult> res, Dictionary<string, int> labels, int k)
    {
        if (res.Count == 0) return 0;
        int lim = Math.Min(res.Count, k);
        double dcg = 0;
        var rels = new List<int>();
        for (int i = 0; i < lim; i++)
        {
            int rel = labels.GetValueOrDefault(res[i].Url ?? "", 0);
            rels.Add(rel);
            dcg += (Math.Pow(2, rel) - 1) / Math.Log2(i + 2);
        }
        rels.Sort((a, b) => b - a);
        double idcg = 0;
        for (int i = 0; i < rels.Count; i++)
            idcg += (Math.Pow(2, rels[i]) - 1) / Math.Log2(i + 2);
        return idcg < 1e-9 ? 0 : dcg / idcg;
    }

    private static double Mrr(List<SearchResult> res, Dictionary<string, int> labels)
    {
        for (int i = 0; i < res.Count; i++)
            if (labels.GetValueOrDefault(res[i].Url ?? "", 0) >= 1)
                return 1.0 / (i + 1);
        return 0;
    }

    private static double Precision(List<SearchResult> res, Dictionary<string, int> labels, int k)
    {
        if (res.Count == 0) return 0;
        int lim = Math.Min(res.Count, k);
        int hits = 0;
        for (int i = 0; i < lim; i++)
            if (labels.GetValueOrDefault(res[i].Url ?? "", 0) >= 1) hits++;
        return (double)hits / lim;
    }

    private static double R(double v) => Math.Round(v, 4);
}
