using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MetaSearch.Models;

namespace MetaSearch.Pages;

/// <summary>회원가입 페이지 — 폼 제출 시 DB에 회원 INSERT</summary>
public class RegisterModel : PageModel
{
    private readonly Services.MemberDao _dao;
    private readonly Services.DbService _db;
    public readonly Services.Lang Lang;

    public RegisterModel(Services.MemberDao dao, Services.DbService db, Services.Lang lang)
    {
        _dao = dao;
        _db = db;
        Lang = lang;
    }

    // 입력값 바인딩
    [BindProperty] public MemberDo Input { get; set; } = new();

    // 비밀번호 확인 필드
    [BindProperty] public string PasswdConfirm { get; set; } = "";

    // 오류 메시지
    public string ErrorMessage { get; set; } = "";

    public void OnGet() { }

    /// <summary>회원가입 폼 제출 처리</summary>
    public IActionResult OnPost()
    {
        // 필수값 검증
        if (string.IsNullOrWhiteSpace(Input.Userid) ||
            string.IsNullOrWhiteSpace(Input.Passwd) ||
            string.IsNullOrWhiteSpace(Input.Name) ||
            string.IsNullOrWhiteSpace(Input.Nickname))
        {
            ErrorMessage = Lang.Get("reg.fillAll");
            return Page();
        }

        // 비밀번호 확인 일치 검증
        if (Input.Passwd != PasswdConfirm)
        {
            ErrorMessage = "비밀번호가 일치하지 않습니다.";
            return Page();
        }

        // 비밀번호 정책 검증 (8자 이상, 영문+숫자+특수문자 포함)
        if (Input.Passwd.Length < 8 ||
            !System.Text.RegularExpressions.Regex.IsMatch(Input.Passwd, @"[A-Za-z]") ||
            !System.Text.RegularExpressions.Regex.IsMatch(Input.Passwd, @"[0-9]") ||
            !System.Text.RegularExpressions.Regex.IsMatch(Input.Passwd, @"[^A-Za-z0-9]"))
        {
            ErrorMessage = "비밀번호는 8자 이상, 영문+숫자+특수문자를 포함해야 합니다.";
            return Page();
        }

        // 아이디 소문자 정규화 + 공백 제거
        Input.Userid = Input.Userid.Trim().ToLowerInvariant();

        // 아이디 유효성 검사 (영문+숫자+밑줄, 3~20자)
        if (!System.Text.RegularExpressions.Regex.IsMatch(Input.Userid, @"^[a-z0-9_]{3,20}$"))
        {
            ErrorMessage = "아이디는 영문 소문자, 숫자, 밑줄(_)만 사용 가능합니다. (3~20자)";
            return Page();
        }

        // 닉네임·이름 길이 제한 + 태그 제거 (XSS 방지)
        Input.Name = StripTags(Input.Name.Trim());
        Input.Nickname = StripTags(Input.Nickname.Trim());
        Input.Email = Input.Email?.Trim() ?? "";

        if (Input.Nickname.Length > 20 || Input.Name.Length > 20)
        {
            ErrorMessage = "이름/닉네임은 20자 이내로 입력해주세요.";
            return Page();
        }

        // 서버 측 이메일 인증 확인 (세션에 저장된 인증 이메일과 비교)
        string? verifiedEmail = HttpContext.Session.GetString("EmailVerified");
        if (verifiedEmail == null || verifiedEmail != Input.Email.Trim().ToLowerInvariant())
        {
            ErrorMessage = "이메일 인증을 완료해주세요.";
            return Page();
        }

        // 이메일 중복 검사 (서버 측 이중 체크)
        using (var conn = _db.OpenConnection())
        {
            string emailLower = Input.Email.Trim().ToLowerInvariant();
            var emailExists = Dapper.SqlMapper.QueryFirstOrDefault<string>(conn,
                "SELECT userid FROM members WHERE LOWER(email) = @emailLower AND (TRIM(status) = 'true' OR TRIM(status) = '1')",
                new { emailLower });
            if (emailExists != null)
            {
                ErrorMessage = "이미 가입된 이메일입니다.";
                return Page();
            }
        }

        // DB에 회원 등록 시도
        int result = _dao.RegisterUser(Input);
        if (result == -1)
        {
            ErrorMessage = Lang.Get("reg.duplicate");
            return Page();
        }

        // 등록 성공 → 홈으로 이동
        TempData["ToastMsg"] = "회원가입이 완료되었습니다. 로그인해주세요.";
        TempData["ToastType"] = "success";
        return Redirect("/");
    }

    /// <summary>HTML 태그 제거 (XSS 방지)</summary>
    private static string StripTags(string input) =>
        System.Text.RegularExpressions.Regex.Replace(input, "<.*?>", "");
}
