using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MetaSearch.Services;

/// <summary>
/// Groq API(OpenAI 호환)를 통해 AI 기능을 제공하는 서비스.
/// 모델: openai/gpt-oss-120b (120B, 무료, Groq)
/// 프롬프트에 할루시네이션 방지 지시 포함.
/// </summary>
public class ClaudeApi
{
    private const string NIM_URL = "https://api.groq.com/openai/v1/chat/completions";
    private const string NIM_MODEL = "openai/gpt-oss-120b";

    private readonly IHttpClientFactory _httpFactory;
    private readonly string _apiKey;

    public ClaudeApi(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _apiKey = config["ApiKeys:GroqApiKey"] ?? "";
    }

    /// <summary>검색어를 13개 카테고리 중 하나로 분류</summary>
    public async Task<string> ClassifyAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "일반";

        // 시스템 프롬프트: 카테고리 목록 + 예시
        string systemMsg =
            "너는 한국어 검색어 분류 AI다. 아래 카테고리 중 정확히 하나만 출력해. 절대 설명하지 마.\n\n" +
            "카테고리:\n" +
            "차량(자동차·오토바이·전기차·트럭·SUV·차종·제조사)\n" +
            "국내장소(한국 도시·관광지·명소·산·해변)\n" +
            "해외장소(한국 외 모든 나라·도시·주(州)·지명)\n" +
            "국내맛집(한국 음식·국내 식당·카페·배달)\n" +
            "해외맛집(외국 음식·해외 식당)\n" +
            "게임(비디오게임·모바일게임·e스포츠)\n" +
            "IT기술(컴퓨터·프로그래밍·AI·소프트웨어·보안)\n" +
            "쇼핑(제품구매·브랜드·가격비교·쇼핑몰)\n" +
            "국내뉴스(한국 정치·경제·사회·사건)\n" +
            "해외뉴스(외국 정치·국제분쟁·글로벌경제)\n" +
            "의료건강(질병·증상·의약품·건강관리)\n" +
            "스포츠(운동종목·경기·선수·구단)\n" +
            "일반(위 항목 해당 없음)\n\n" +
            "예시:\n테슬라 → 차량\n제주도 → 국내장소\n파리 → 해외장소\n삼겹살 → 국내맛집\n" +
            "라멘 → 해외맛집\n롤 → 게임\n파이썬 → IT기술\n아이폰 가격 → 쇼핑\n" +
            "코스피 → 국내뉴스\n트럼프 → 해외뉴스\n독감 → 의료건강\n손흥민 → 스포츠";

        string result = (await CallNimAsync(query, systemMsg, 4096)).Trim().Replace(" ", "");

        string[] valid = { "차량","국내장소","해외장소","국내맛집","해외맛집","게임","IT기술","쇼핑","국내뉴스","해외뉴스","의료건강","스포츠","일반" };
        foreach (string v in valid)
            if (result.Contains(v)) return v;
        return "일반";
    }

    /// <summary>관련 검색어 5개 생성 (JSON 배열 문자열 반환)</summary>
    public async Task<string> GetRelatedAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "[]";

        string prompt = $"검색어 '{query}'과(와) 관련된 검색어 5개를 쉼표로만 나열해. 다른 말은 절대 하지 마.\n예) 강남 맛집, 홍대 카페, 이태원 레스토랑, 성수동 맛집, 여의도 레스토랑";
        string result = (await CallNimAsync(prompt, null, 4096)).Trim();

        var sb = new StringBuilder("[");
        int count = 0;
        foreach (string raw in result.Split(','))
        {
            string t = raw.Trim();
            if (t.Length > 2 && char.IsDigit(t[0]) && (t[1] == '.' || t[1] == ')'))
                t = t[2..].Trim();
            if (string.IsNullOrEmpty(t) || t.Length > 50) continue;
            if (count > 0) sb.Append(',');
            sb.Append('"').Append(t.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            if (++count >= 5) break;
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>RRF 상위 결과를 관련도 순으로 재정렬 — 120B 실패 시 qwen 재시도</summary>
    public async Task<string> RankResultsAsync(string query, string resultsText)
    {
        string systemMsg =
            "You are a search result ranker. Given a query and numbered results, output the numbers (0-based) in order of relevance, comma-separated. " +
            "Example: 2,0,4,1,3. Output ONLY numbers and commas.";

        string userMsg = $"검색어: \"{query}\"\n결과:\n{resultsText}";

        // 1차: 120B reasoning 모델 시도
        string raw = (await CallModelAsync(NIM_MODEL, userMsg, systemMsg, 4096)).Trim();
        string cleaned = Regex.Replace(raw, @"[^0-9,]", "").Trim(',');

        // 유효 결과 부족 시 qwen fallback
        if (string.IsNullOrEmpty(cleaned) || !cleaned.Contains(','))
        {
            raw = (await CallModelAsync(FALLBACK_MODEL, userMsg, systemMsg, 1024)).Trim();
            cleaned = Regex.Replace(raw, @"[^0-9,]", "").Trim(',');
        }

        return cleaned;
    }

    /// <summary>검색 결과 URL들의 관련성을 0·1·2로 레이블링 — 120B 실패 시 qwen 재시도</summary>
    public async Task<int[]> LabelRelevanceAsync(string query, string[] titles, string[] snippets)
    {
        int n = titles.Length;
        if (n == 0) return Array.Empty<int>();

        string systemMsg = "You are a search result relevance evaluator. Rate each item as 0(irrelevant), 1(partial), or 2(highly relevant). Output ONLY numbers separated by newlines. No explanations.";
        var sb = new StringBuilder();
        sb.AppendLine($"검색어: \"{query}\"");
        sb.AppendLine("각 항목 관련성을 0·1·2로만 평가해. 항목 수만큼 숫자를 줄바꿈으로 출력:");
        for (int i = 0; i < n; i++)
            sb.AppendLine($"{i}. {titles[i]} — {(snippets[i].Length > 80 ? snippets[i][..80] : snippets[i])}");

        string prompt = sb.ToString();

        // 1차: 120B reasoning 모델 (정확도 높음)
        string raw = (await CallModelAsync(NIM_MODEL, prompt, systemMsg, 4096)).Trim();
        var nums = Regex.Matches(raw, @"\b[0-2]\b");

        // 파싱 결과가 절반 미만이면 qwen으로 재시도
        if (nums.Count < n / 2)
        {
            raw = (await CallModelAsync(FALLBACK_MODEL, prompt, systemMsg, 1024)).Trim();
            nums = Regex.Matches(raw, @"\b[0-2]\b");
        }

        var scores = new int[n];
        for (int i = 0; i < Math.Min(n, nums.Count); i++)
            scores[i] = int.Parse(nums[i].Value);
        return scores;
    }

    /// <summary>검색어에 대한 AI 요약 생성 (Google AI Overview 스타일)</summary>
    public async Task<string> SummarizeQueryAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "";

        string systemMsg =
            "너는 검색 도우미 AI야. 사용자의 검색어에 대해 간결하고 유용한 요약 정보를 3~5문장으로 제공해. " +
            "핵심만 정리하고, 마크다운 없이 일반 텍스트로 출력해. " +
            "절대 거짓 정보를 지어내지 마. 확실하지 않은 내용은 추측이라고 밝히거나 아예 언급하지 마. " +
            "존재하지 않거나 아직 발표되지 않은 제품·사건은 '아직 공식 정보가 없습니다'라고 솔직하게 답해. " +
            "모르는 내용은 '관련 정보를 찾지 못했습니다.'라고 답해.";

        string result = await CallNimAsync($"검색어: {query}", systemMsg, 4096);
        if (result == "일반" || string.IsNullOrWhiteSpace(result))
            return "";
        return result;
    }

    private const string FALLBACK_MODEL = "qwen/qwen3.8-27b";

    // Groq API 호출 — 120B 실패 시 qwen fallback 지원
    private async Task<string> CallNimAsync(string userMessage, string systemMessage = null, int maxTokens = 256, bool useFallback = true)
    {
        // 1차: 120B 모델
        string result = await CallModelAsync(NIM_MODEL, userMessage, systemMessage, maxTokens);
        if (!string.IsNullOrWhiteSpace(result) && result != "일반")
            return result;

        // 2차: fallback → qwen (reasoning 토큰 문제 없음, 작은 max_tokens OK)
        if (useFallback)
        {
            int fallbackTokens = Math.Min(maxTokens, 1024);
            return await CallModelAsync(FALLBACK_MODEL, userMessage, systemMessage, fallbackTokens);
        }
        return result;
    }

    // 단일 모델 API 호출
    private async Task<string> CallModelAsync(string model, string userMessage, string systemMessage, int maxTokens)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var client = _httpFactory.CreateClient();
                // 120B reasoning 모델은 사고 시간이 길어 타임아웃 여유 확보
            client.Timeout = TimeSpan.FromSeconds(systemMessage != null ? 90 : 15);

                var messages = new List<object>();
                if (!string.IsNullOrEmpty(systemMessage))
                    messages.Add(new { role = "system", content = systemMessage });
                messages.Add(new { role = "user", content = userMessage });

                var body = JsonSerializer.Serialize(new
                {
                    model,
                    max_tokens = maxTokens,
                    temperature = 0.1,
                    messages
                });

                var request = new HttpRequestMessage(HttpMethod.Post, NIM_URL)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Authorization", "Bearer " + _apiKey);

                var response = await client.SendAsync(request);

                if ((int)response.StatusCode == 429)
                {
                    if (attempt == 0) { await Task.Delay(2000); continue; }
                    return "일반";
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                string text = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "";

                // <think>...</think> 블록 제거 (reasoning 모델 대응)
                int thinkEnd = text.LastIndexOf("</think>", StringComparison.Ordinal);
                if (thinkEnd >= 0) text = text[(thinkEnd + 8)..];
                return text.Trim();
            }
            catch
            {
                if (attempt == 0) { await Task.Delay(1000); continue; }
                return "일반";
            }
        }
        return "일반";
    }
}
