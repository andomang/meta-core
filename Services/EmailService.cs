using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace MetaSearch.Services;

/// <summary>이메일 인증 서비스 — Brevo API로 인증코드 발송 (300통/일 무료).</summary>
public class EmailService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;

    // 인증코드 임시 저장 (이메일 → 코드+만료시각)
    private static readonly ConcurrentDictionary<string, (string Code, DateTime Expires)> _codes = new();

    public EmailService(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _http = httpFactory.CreateClient();
    }

    /// <summary>6자리 인증코드 생성 후 Brevo API로 발송 (5분 유효)</summary>
    public async Task<bool> SendVerificationCodeAsync(string toEmail)
    {
        try
        {
            string code = Random.Shared.Next(100000, 999999).ToString();
            _codes[toEmail.ToLowerInvariant()] = (code, DateTime.UtcNow.AddMinutes(5));

            string apiKey = _config["Brevo:ApiKey"] ?? "";
            string senderEmail = _config["Brevo:SenderEmail"] ?? "";
            string senderName = _config["Brevo:SenderName"] ?? "Meta Search";

            // Brevo Transactional Email API
            var payload = new
            {
                sender = new { name = senderName, email = senderEmail },
                to = new[] { new { email = toEmail } },
                subject = "[Meta Search] 이메일 인증 코드",
                textContent = $"Meta Search 회원가입 인증 코드입니다.\n\n인증 코드: {code}\n\n5분 이내에 입력해주세요."
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
            request.Headers.Add("api-key", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>인증코드 검증 (일치하면 true, 사용 후 삭제)</summary>
    public bool VerifyCode(string email, string code)
    {
        string key = email.ToLowerInvariant();
        if (!_codes.TryGetValue(key, out var entry)) return false;
        if (DateTime.UtcNow > entry.Expires)
        {
            _codes.TryRemove(key, out _);
            return false;
        }
        if (entry.Code != code.Trim()) return false;

        _codes.TryRemove(key, out _);
        return true;
    }
}
