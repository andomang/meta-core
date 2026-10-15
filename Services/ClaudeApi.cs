using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MetaSearch.Services;

/// <summary>
/// Groq API(OpenAI 호환)를 통해 AI 기능을 제공하는 서비스.
/// 모델: qwen/qwen3.8-27b (한국어 특화, 무료, 30 RPM)
/// 기능: 검색어 분류, 관련 검색어 생성, 결과 재정렬.
/// </summary>
public class ClaudeApi
{
    private const string NIM_URL = "https://api.groq.com/openai/v1/chat/completions";
    private const string NIM_MODEL = "qwen/qwen3.8-27b";

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

        string result = (await CallNimAsync(query, systemMsg)).Trim().Replace(" ", "");

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
        string result = (await CallNimAsync(prompt)).Trim();

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

    /// <summary>RRF 상위 결과를 관련도 순으로 재정렬 (인덱스 순서 문자열 반환)</summary>
    public async Task<string> RankResultsAsync(string query, string resultsText)
    {
        string systemMsg =
            "너는 검색 결과 관련도 평가 AI다. " +
            "검색어와 번호가 붙은 결과 목록을 받아 관련도 높은 순서로 번호(0부터)만 쉼표로 나열해. " +
            "예: 2,0,4,1,3   설명하지 마. 숫자와 쉼표만 출력해.";

        string userMsg = $"검색어: \"{query}\"\n결과:\n{resultsText}";
        string raw = (await CallNimAsync(userMsg, systemMsg)).Trim();
        return Regex.Replace(raw, @"[^0-9,]", "").Trim(',');
    }

    /// <summary>검색 결과 URL들의 관련성을 0·1·2로 레이블링 (평가용)</summary>
    public async Task<int[]> LabelRelevanceAsync(string query, string[] titles, string[] snippets)
    {
        int n = titles.Length;
        if (n == 0) return Array.Empty<int>();

        // systemMessage 추가 → timeout 60초 적용 + thinking 억제로 속도 향상
        string systemMsg = "너는 검색 결과 관련성 평가 AI다. 각 항목을 0(무관련)·1(부분관련)·2(매우관련)으로 평가해. 숫자만 줄바꿈으로 출력해. 설명·이유·생각 절대 금지. /no_think";
        var sb = new StringBuilder();
        sb.AppendLine($"검색어: \"{query}\"");
        sb.AppendLine("각 항목 관련성을 0·1·2로만 평가해. 항목 수만큼 숫자를 줄바꿈으로 출력:");
        for (int i = 0; i < n; i++)
            sb.AppendLine($"{i}. {titles[i]} — {(snippets[i].Length > 80 ? snippets[i][..80] : snippets[i])}");

        string raw = (await CallNimAsync(sb.ToString(), systemMsg, n * 3 + 20)).Trim();

        // 숫자만 추출 (0~2)
        var scores = new int[n];
        var nums = Regex.Matches(raw, @"\b[0-2]\b");
        for (int i = 0; i < Math.Min(n, nums.Count); i++)
            scores[i] = int.Parse(nums[i].Value);
        return scores;
    }

    // Groq API 호출 (OpenAI 호환)
    private async Task<string> CallNimAsync(string userMessage, string systemMessage = null, int maxTokens = 128)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var client = _httpFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(systemMessage != null ? 60 : 15);

                var messages = new List<object>();
                if (!string.IsNullOrEmpty(systemMessage))
                    messages.Add(new { role = "system", content = systemMessage });
                messages.Add(new { role = "user", content = userMessage });

                var body = JsonSerializer.Serialize(new
                {
                    model = NIM_MODEL,
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

                // 429 Rate Limit → 2초 후 재시도
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

                // <think>...</think> 블록 제거 (Qwen3 추론 모델)
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
