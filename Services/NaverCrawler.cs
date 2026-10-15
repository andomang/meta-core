using System.Text.Json;
using System.Text.RegularExpressions;
using MetaSearch.Models;

namespace MetaSearch.Services;

/// <summary>
/// 네이버 검색 API 크롤러.
/// webkr(웹문서) + blog(블로그) 혼합 검색으로 국내 쿼리 커버리지 향상.
/// </summary>
public class NaverCrawler
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _clientId;
    private readonly string _clientSecret;

    public NaverCrawler(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _clientId = config["ApiKeys:NaverClientId"] ?? "";
        _clientSecret = config["ApiKeys:NaverClientSecret"] ?? "";
    }

    /// <summary>webkr 절반 + blog 절반 혼합 검색</summary>
    public async Task<List<SearchResult>> SearchAsync(string query, int count = 10, int start = 1)
    {
        int half = count / 2;
        int remain = count - half;

        var webTask = FetchEndpointAsync("webkr", query, half, start);
        var blogTask = FetchEndpointAsync("blog", query, remain, 1);
        await Task.WhenAll(webTask, blogTask);

        // URL 중복 제거 후 병합
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<SearchResult>();
        foreach (var r in webTask.Result)
            if (!string.IsNullOrEmpty(r.Url) && seen.Add(r.Url)) merged.Add(r);
        foreach (var r in blogTask.Result)
            if (!string.IsNullOrEmpty(r.Url) && seen.Add(r.Url)) merged.Add(r);
        return merged;
    }

    // 네이버 검색 엔드포인트 호출
    private async Task<List<SearchResult>> FetchEndpointAsync(string endpoint, string query, int count, int start)
    {
        var results = new List<SearchResult>();
        if (count <= 0) return results;
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            string url = $"https://openapi.naver.com/v1/search/{endpoint}.json?query={Uri.EscapeDataString(query)}&display={count}&start={start}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Naver-Client-Id", _clientId);
            request.Headers.Add("X-Naver-Client-Secret", _clientSecret);

            var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return results;

            foreach (var item in items.EnumerateArray())
            {
                string title = item.GetPropertyOrDefault("title");
                string link = item.GetPropertyOrDefault("link");
                if (string.IsNullOrEmpty(link) && item.TryGetProperty("posturl", out var pu))
                    link = pu.GetString() ?? "";
                string desc = item.GetPropertyOrDefault("description");

                if (string.IsNullOrEmpty(link)) continue;

                results.Add(new SearchResult
                {
                    Title = StripHtml(title),
                    Url = link,
                    Description = StripHtml(desc),
                    Source = "naver"
                });
            }
        }
        catch { }
        return results;
    }

    // HTML 태그 제거
    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        string text = Regex.Replace(html, @"<[^>]+>", "");
        return text.Replace("&lt;", "<").Replace("&gt;", ">")
                   .Replace("&amp;", "&").Replace("&quot;", "\"")
                   .Replace("&#39;", "'").Replace("&apos;", "'")
                   .Replace("&nbsp;", " ").Trim();
    }
}
