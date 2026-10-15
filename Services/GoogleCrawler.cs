using System.Text;
using System.Text.Json;
using MetaSearch.Models;

namespace MetaSearch.Services;

/// <summary>
/// Serper.dev API 기반 구글 검색 크롤러.
/// POST https://google.serper.dev/search (gl=kr, hl=ko)
/// </summary>
public class GoogleCrawler
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _apiKey;

    public GoogleCrawler(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _apiKey = config["ApiKeys:SerperApiKey"] ?? "";
    }

    /// <summary>Serper.dev로 구글 검색 결과 가져오기 (page=1~: Serper page 파라미터)</summary>
    public async Task<List<SearchResult>> SearchAsync(string query, int count = 10, int page = 1)
    {
        var results = new List<SearchResult>();
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);

            var body = JsonSerializer.Serialize(new
            {
                q = query,
                gl = "kr",
                hl = "ko",
                num = Math.Min(count, 100),
                page = page  // Serper API: 1=첫 페이지, 2=11~20번, 3=21~30번
            });

            var request = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/search")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-API-KEY", _apiKey);

            var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("organic", out var organic)) return results;

            foreach (var item in organic.EnumerateArray())
            {
                string title = item.GetPropertyOrDefault("title");
                string link = item.GetPropertyOrDefault("link");
                string snippet = item.GetPropertyOrDefault("snippet");
                string imgUrl = item.GetPropertyOrDefault("imageUrl");

                if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(title)) continue;

                results.Add(new SearchResult
                {
                    Title = title,
                    Url = link,
                    Description = snippet,
                    Source = "google",
                    ImageUrl = imgUrl
                });
            }
        }
        catch { }
        return results;
    }
}

/// <summary>JsonElement 확장 메서드</summary>
public static class JsonElementExtensions
{
    public static string GetPropertyOrDefault(this JsonElement el, string name)
    {
        return el.TryGetProperty(name, out var val) ? val.GetString() ?? "" : "";
    }
}
