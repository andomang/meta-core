using System.Text.Json;
using MetaSearch.Models;

namespace MetaSearch.Services;

/// <summary>
/// Brave Search API 기반 독립 크롤러.
/// GET https://api.search.brave.com/res/v1/web/search
/// 무료 티어: 2,000콜/월 (Google·Bing 인덱스 미사용 독립 크롤러)
/// </summary>
public class BraveCrawler
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _apiKey;

    public BraveCrawler(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _apiKey = config["ApiKeys:BraveApiKey"] ?? "";
    }

    /// <summary>Brave Search로 결과 가져오기 (offset: 페이지네이션 시작 인덱스)</summary>
    public async Task<List<SearchResult>> SearchAsync(string query, int count = 10, int offset = 0)
    {
        var results = new List<SearchResult>();

        // API 키 미설정 시 빈 결과 반환 (다른 엔진에는 영향 없음)
        if (string.IsNullOrEmpty(_apiKey) || _apiKey == "YOUR_BRAVE_API_KEY")
            return results;

        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);

            // Brave API: GET 방식, 쿼리스트링 파라미터
            var count_ = Math.Min(count, 20); // Brave 최대 20개
            var url = $"https://api.search.brave.com/res/v1/web/search" +
                      $"?q={Uri.EscapeDataString(query)}" +
                      $"&count={count_}" +
                      $"&offset={offset}" +
                      $"&country=KR" +       // 한국 지역 결과 우선
                      $"&search_lang=ko" +   // 한국어 우선
                      $"&ui_lang=ko-KR";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            // Accept-Encoding 직접 지정 금지 — HttpClient가 자동 처리하지 않아 gzip 원문이 그대로 와서 JSON 파싱 실패
            request.Headers.Add("X-Subscription-Token", _apiKey);

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return results;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            // web.results 배열에서 결과 추출
            if (!doc.RootElement.TryGetProperty("web", out var web)) return results;
            if (!web.TryGetProperty("results", out var webResults)) return results;

            foreach (var item in webResults.EnumerateArray())
            {
                string title = item.GetPropertyOrDefault("title");
                string link = item.GetPropertyOrDefault("url");
                string snippet = "";

                // description 또는 extra_snippets 사용
                if (item.TryGetProperty("description", out var desc))
                    snippet = desc.GetString() ?? "";

                if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(title)) continue;

                results.Add(new SearchResult
                {
                    Title = title,
                    Url = link,
                    Description = snippet,
                    Source = "brave"
                });
            }
        }
        catch { }
        return results;
    }
}
