using System.Text.Json;
using System.Text.RegularExpressions;
using MetaSearch.Models;

namespace MetaSearch.Services;

/// <summary>
/// 카카오 검색 API 기반 다음(Daum) 검색 크롤러.
/// web(웹문서) + blog(블로그) 혼합 검색.
/// </summary>
public class DaumCrawler
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _apiKey;

    public DaumCrawler(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _apiKey = config["ApiKeys:KakaoApiKey"] ?? "";
    }

    /// <summary>web 절반 + blog 절반 혼합 검색</summary>
    public async Task<List<SearchResult>> SearchAsync(string query, int count = 10, int page = 1)
    {
        int half = count / 2;
        int remain = count - half;

        var webTask = FetchEndpointAsync("web", query, half, page);
        var blogTask = FetchEndpointAsync("blog", query, remain, page);
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

    // 카카오 검색 엔드포인트 호출
    private async Task<List<SearchResult>> FetchEndpointAsync(string endpoint, string query, int count, int page)
    {
        var results = new List<SearchResult>();
        if (count <= 0) return results;
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            string url = $"https://dapi.kakao.com/v2/search/{endpoint}?query={Uri.EscapeDataString(query)}&size={count}&page={page}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", "KakaoAK " + _apiKey);

            var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("documents", out var docs)) return results;

            foreach (var d in docs.EnumerateArray())
            {
                string title = d.GetPropertyOrDefault("title");
                string docUrl = d.GetPropertyOrDefault("url");
                string contents = d.GetPropertyOrDefault("contents");
                string thumbnail = d.GetPropertyOrDefault("thumbnail");

                if (string.IsNullOrEmpty(docUrl)) continue;

                results.Add(new SearchResult
                {
                    Title = StripHtml(title),
                    Url = docUrl,
                    Description = StripHtml(contents),
                    Source = "daum",
                    ImageUrl = thumbnail
                });
            }
        }
        catch { }
        return results;
    }

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
