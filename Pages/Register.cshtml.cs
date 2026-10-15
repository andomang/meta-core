using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MetaSearch.Models;

namespace MetaSearch.Pages;

/// <summary>회원가입 페이지 — 폼 제출 시 DB에 회원 INSERT</summary>
public class RegisterModel : PageModel
{
    private readonly Services.MemberDao _dao;
    public readonly Services.Lang Lang;

    public RegisterModel(Services.MemberDao dao, Services.Lang lang)
    {
        _dao = dao;
        Lang = lang;
    }

    // 입력값 바인딩
    [BindProperty] public MemberDo Input { get; set; } = new();

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
}
