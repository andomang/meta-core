using System.Text.Json;
using System.Text.RegularExpressions;

namespace MetaSearch.Services;

/// <summary>
/// AI 분류 키워드에 따라 외부 API를 호출하여 추가 검색 결과를 제공하는 서비스.
/// 카카오 로컬, 네이버 쇼핑/뉴스/블로그, YouTube, Wikipedia, 환율, 날씨 지원.
/// 모든 메서드는 실패 시 "{}" 반환.
/// </summary>
public class ExtraApi
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _naverId;
    private readonly string _naverSecret;
    private readonly string _kakaoKey;
    private readonly string _youtubeKey;
    private readonly string _weatherKey;

    public ExtraApi(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _naverId = config["ApiKeys:NaverClientId"] ?? "";
        _naverSecret = config["ApiKeys:NaverClientSecret"] ?? "";
        _kakaoKey = config["ApiKeys:KakaoApiKey"] ?? "";
        _youtubeKey = config["ApiKeys:YouTubeApiKey"] ?? "";
        _weatherKey = config["ApiKeys:WeatherApiKey"] ?? "";
    }

    /// <summary>검색어+키워드에 맞는 외부 API 결과를 JSON으로 반환</summary>
    public async Task<string> GetExtraAsync(string query, string keyword)
    {
        try
        {
            if (IsExchangeQuery(query)) return await GetExchangeRateAsync();

            return keyword switch
            {
                "국내장소" or "국내맛집" => await GetKakaoPlacesAsync(query),
                "쇼핑" => await GetNaverShoppingAsync(query),
                "차량" or "게임" or "스포츠" => await GetYoutubeAsync(query),
                "국내뉴스" => await GetNaverNewsAsync(query),
                "IT기술" => await GetWikipediaAsync(query),
                "해외장소" => await GetWeatherOrWikiAsync(query),
                "해외맛집" or "해외뉴스" or "의료건강" => await GetWikipediaAsync(query),
                "일반" => await GetNaverBlogAsync(query),
                _ => "{}"
            };
        }
        catch { return "{}"; }
    }

    // 환율 관련 검색어 감지
    private static bool IsExchangeQuery(string q)
    {
        q = (q ?? "").ToLower();
        return q.Contains("환율") || q.Contains("달러") || q.Contains("엔화") ||
               q.Contains("유로") || q.Contains("위안") || q.Contains("usd") ||
               q.Contains("jpy") || q.Contains("eur") || q.Contains("cny") || q.Contains("gbp");
    }

    // ── 카카오 로컬 API ──
    private async Task<string> GetKakaoPlacesAsync(string query)
    {
        string url = $"https://dapi.kakao.com/v2/local/search/keyword.json?query={Uri.EscapeDataString(query)}&size=5";
        string json = await HttpGetAsync(url, "KakaoAK " + _kakaoKey);
        if (string.IsNullOrEmpty(json)) return "{}";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("documents", out var docs)) return "{}";

            var items = new List<object>();
            foreach (var d in docs.EnumerateArray())
            {
                string addr = d.GetPropertyOrDefault("road_address_name");
                if (string.IsNullOrEmpty(addr)) addr = d.GetPropertyOrDefault("address_name");
                items.Add(new
                {
                    name = d.GetPropertyOrDefault("place_name"),
                    address = addr,
                    phone = d.GetPropertyOrDefault("phone"),
                    category = d.GetPropertyOrDefault("category_name"),
                    url = d.GetPropertyOrDefault("place_url"),
                    lat = d.GetPropertyOrDefault("y"),
                    lng = d.GetPropertyOrDefault("x")
                });
            }
            if (items.Count == 0) return "{}";
            return JsonSerializer.Serialize(new { type = "places", items });
        }
        catch { return "{}"; }
    }

    // ── 네이버 쇼핑 API ──
    private async Task<string> GetNaverShoppingAsync(string query)
    {
        string url = $"https://openapi.naver.com/v1/search/shop.json?query={Uri.EscapeDataString(query)}&display=6&sort=sim";
        string json = await HttpGetWithNaverAsync(url);
        if (string.IsNullOrEmpty(json)) return "{}";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var raw)) return "{}";

            var items = new List<object>();
            foreach (var item in raw.EnumerateArray())
            {
                string price = item.GetPropertyOrDefault("lprice");
                if (price == "0") price = "";
                items.Add(new
                {
                    title = StripHtml(item.GetPropertyOrDefault("title")),
                    price,
                    mall = item.GetPropertyOrDefault("mallName"),
                    image = item.GetPropertyOrDefault("image"),
                    link = item.GetPropertyOrDefault("link")
                });
            }
            if (items.Count == 0) return "{}";
            return JsonSerializer.Serialize(new { type = "shopping", items });
        }
        catch { return "{}"; }
    }

    // ── 네이버 뉴스 API ──
    private async Task<string> GetNaverNewsAsync(string query)
    {
        string url = $"https://openapi.naver.com/v1/search/news.json?query={Uri.EscapeDataString(query)}&display=5&sort=date";
        string json = await HttpGetWithNaverAsync(url);
        if (string.IsNullOrEmpty(json)) return "{}";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var raw)) return "{}";

            var items = new List<object>();
            foreach (var item in raw.EnumerateArray())
            {
                items.Add(new
                {
                    title = StripHtml(item.GetPropertyOrDefault("title")),
                    desc = StripHtml(item.GetPropertyOrDefault("description")),
                    link = item.GetPropertyOrDefault("link"),
                    pub = item.GetPropertyOrDefault("pubDate")
                });
            }
            if (items.Count == 0) return "{}";
            return JsonSerializer.Serialize(new { type = "news", items });
        }
        catch { return "{}"; }
    }

    // ── 네이버 블로그 API (일반 카테고리 폴백) ──
    private async Task<string> GetNaverBlogAsync(string query)
    {
        string url = $"https://openapi.naver.com/v1/search/blog.json?query={Uri.EscapeDataString(query)}&display=5&sort=sim";
        string json = await HttpGetWithNaverAsync(url);
        if (string.IsNullOrEmpty(json)) return "{}";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var raw)) return "{}";

            var items = new List<object>();
            foreach (var item in raw.EnumerateArray())
            {
                items.Add(new
                {
                    title = StripHtml(item.GetPropertyOrDefault("title")),
                    desc = StripHtml(item.GetPropertyOrDefault("description")),
                    link = item.GetPropertyOrDefault("link"),
                    blogger = item.GetPropertyOrDefault("bloggername"),
                    date = item.GetPropertyOrDefault("postdate")
                });
            }
            if (items.Count == 0) return "{}";
            return JsonSerializer.Serialize(new { type = "blog", items });
        }
        catch { return "{}"; }
    }

    // ── YouTube Data API ──
    private async Task<string> GetYoutubeAsync(string query)
    {
        string url = $"https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&maxResults=4&q={Uri.EscapeDataString(query)}&key={_youtubeKey}";
        string json = await HttpGetAsync(url);
        if (string.IsNullOrEmpty(json)) return "{}";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var raw)) return "{}";

            var items = new List<object>();
            foreach (var item in raw.EnumerateArray())
            {
                string videoId = "";
                if (item.TryGetProperty("id", out var idObj))
                    videoId = idObj.GetPropertyOrDefault("videoId");
                if (string.IsNullOrEmpty(videoId)) continue;

                string title = "", channel = "", thumb = "";
                if (item.TryGetProperty("snippet", out var snippet))
                {
                    title = snippet.GetPropertyOrDefault("title");
                    channel = snippet.GetPropertyOrDefault("channelTitle");
                    if (snippet.TryGetProperty("thumbnails", out var thumbs))
                    {
                        if (thumbs.TryGetProperty("medium", out var med))
                            thumb = med.GetPropertyOrDefault("url");
                        else if (thumbs.TryGetProperty("default", out var def))
                            thumb = def.GetPropertyOrDefault("url");
                    }
                }
                items.Add(new { id = videoId, title, channel, thumb });
            }
            if (items.Count == 0) return "{}";
            return JsonSerializer.Serialize(new { type = "youtube", items });
        }
        catch { return "{}"; }
    }

    // ── Wikipedia REST API ──
    private async Task<string> GetWikipediaAsync(string query)
    {
        string result = await FetchWikiSummaryAsync("ko", query);
        if (result != "{}") return result;
        return await FetchWikiSummaryAsync("en", query);
    }

    private async Task<string> FetchWikiSummaryAsync(string lang, string query)
    {
        string url = $"https://{lang}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(query)}";
        string json = await HttpGetAsync(url);
        if (string.IsNullOrEmpty(json)) return "{}";

        try
        {
            using var doc = JsonDocument.Parse(json);
            string pageType = doc.RootElement.GetPropertyOrDefault("type");
            if (pageType == "disambiguation" || pageType == "no-extract") return "{}";

            string extract = doc.RootElement.GetPropertyOrDefault("extract");
            if (string.IsNullOrWhiteSpace(extract)) return "{}";
            if (extract.Length > 200) extract = extract[..200] + "...";

            string thumbnail = "";
            if (doc.RootElement.TryGetProperty("thumbnail", out var tb))
                thumbnail = tb.GetPropertyOrDefault("source");

            string pageUrl = "";
            if (doc.RootElement.TryGetProperty("content_urls", out var cu) &&
                cu.TryGetProperty("desktop", out var desktop))
                pageUrl = desktop.GetPropertyOrDefault("page");

            string title = doc.RootElement.GetPropertyOrDefault("title");
            return JsonSerializer.Serialize(new { type = "wiki", title, extract, thumbnail, url = pageUrl });
        }
        catch { return "{}"; }
    }

    // ── 날씨 (해외장소: 날씨 우선, 실패 시 Wikipedia) ──
    private static readonly Dictionary<string, string> _cityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        {"파리","Paris"},{"런던","London"},{"뉴욕","New York"},{"도쿄","Tokyo"},{"오사카","Osaka"},
        {"베이징","Beijing"},{"상하이","Shanghai"},{"방콕","Bangkok"},{"싱가포르","Singapore"},
        {"로스앤젤레스","Los Angeles"},{"LA","Los Angeles"},{"시드니","Sydney"},{"두바이","Dubai"},
        {"로마","Rome"},{"바르셀로나","Barcelona"},{"암스테르담","Amsterdam"},{"베를린","Berlin"},
        {"마드리드","Madrid"},{"홍콩","Hong Kong"},{"하노이","Hanoi"},{"호치민","Ho Chi Minh City"},
        {"타이베이","Taipei"},{"자카르타","Jakarta"},{"쿠알라룸푸르","Kuala Lumpur"},{"마닐라","Manila"},
        {"뭄바이","Mumbai"},{"델리","Delhi"},{"카이로","Cairo"},{"모스크바","Moscow"},{"이스탄불","Istanbul"},
        {"시카고","Chicago"},{"토론토","Toronto"},{"밴쿠버","Vancouver"},{"시애틀","Seattle"},
        {"샌프란시스코","San Francisco"},{"라스베가스","Las Vegas"},
    };

    private async Task<string> GetWeatherOrWikiAsync(string query)
    {
        string wx = await GetWeatherAsync(query);
        return wx != "{}" ? wx : await GetWikipediaAsync(query);
    }

    private async Task<string> GetWeatherAsync(string query)
    {
        string cityQuery = Regex.Replace(query.Trim(), @"\s*(날씨|기온|기후|weather|오늘|내일)\s*", " ", RegexOptions.IgnoreCase).Trim();
        cityQuery = Regex.Replace(cityQuery, @"\s+", " ").Trim();
        if (_cityMap.TryGetValue(cityQuery, out var mapped)) cityQuery = mapped;

        string url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(cityQuery)}&appid={_weatherKey}&units=metric&lang=ko";
        string json = await HttpGetAsync(url);
        if (string.IsNullOrEmpty(json)) return "{}";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("cod", out var cod) && cod.ToString() != "200") return "{}";

            var main = doc.RootElement.GetProperty("main");
            string temp = main.GetProperty("temp").GetDouble().ToString("F1");
            string feels = main.GetProperty("feels_like").GetDouble().ToString("F1");
            string humidity = main.GetProperty("humidity").ToString();

            string wind = "";
            if (doc.RootElement.TryGetProperty("wind", out var w))
                wind = w.GetProperty("speed").ToString();

            string desc = "", icon = "";
            if (doc.RootElement.TryGetProperty("weather", out var wArr))
            {
                var first = wArr[0];
                desc = first.GetPropertyOrDefault("description");
                icon = first.GetPropertyOrDefault("icon");
            }

            string city = doc.RootElement.GetPropertyOrDefault("name");
            string country = "";
            if (doc.RootElement.TryGetProperty("sys", out var sys))
                country = sys.GetPropertyOrDefault("country");

            return JsonSerializer.Serialize(new { type = "weather", city, country, temp, feels, humidity, wind, desc, icon });
        }
        catch { return "{}"; }
    }

    // ── 환율 API ──
    private async Task<string> GetExchangeRateAsync()
    {
        string json = await HttpGetAsync("https://open.er-api.com/v6/latest/USD");
        if (string.IsNullOrEmpty(json)) return "{}";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetPropertyOrDefault("result") != "success") return "{}";

            var ratesEl = doc.RootElement.GetProperty("rates");
            double krwPerUsd = ratesEl.GetProperty("KRW").GetDouble();

            var rates = new Dictionary<string, string>
            {
                ["USD"] = krwPerUsd.ToString("F2")
            };
            foreach (string code in new[] { "JPY", "EUR", "CNY", "GBP" })
            {
                if (!ratesEl.TryGetProperty(code, out var v)) continue;
                double perUsd = v.GetDouble();
                if (perUsd == 0) continue;
                rates[code] = (krwPerUsd / perUsd).ToString("F2");
            }

            string updated = doc.RootElement.GetPropertyOrDefault("time_last_update_utc");
            return JsonSerializer.Serialize(new { type = "exchange", rates, updated });
        }
        catch { return "{}"; }
    }

    // ── HTTP 헬퍼 ──
    private async Task<string> HttpGetAsync(string url, string authHeader = null)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept-Language", "ko-KR,ko;q=0.9,en;q=0.8");
            if (!string.IsNullOrEmpty(authHeader))
                request.Headers.Add("Authorization", authHeader);
            var response = await client.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }
        catch { return ""; }
    }

    private async Task<string> HttpGetWithNaverAsync(string url)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Naver-Client-Id", _naverId);
            request.Headers.Add("X-Naver-Client-Secret", _naverSecret);
            var response = await client.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }
        catch { return ""; }
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        return Regex.Replace(html, "<[^>]+>", "");
    }
}
