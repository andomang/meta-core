namespace MetaSearch.Models;

/// <summary>
/// 회원 정보 도메인 객체 (DTO).
/// DB members 테이블 구조에 대응하며, 회원가입·로그인·설정 등에서 사용한다.
/// </summary>
public class MemberDo
{
    // 회원 아이디 (PK)
    public string Userid { get; set; } = "";
    // 비밀번호 (MD5 해시 저장)
    public string Passwd { get; set; } = "";
    // 실명
    public string Name { get; set; } = "";
    // 닉네임 (화면 표시용)
    public string Nickname { get; set; } = "";
    // 이메일
    public string Email { get; set; } = "";
    // 계정 활성화 상태
    public bool Status { get; set; }
    // 회원 등급 (0: 일반, 1: 관리자)
    public int Ugrade { get; set; }

    public MemberDo() { }

    /// <summary>핵심 5개 필드 초기화 생성자 (회원가입용)</summary>
    public MemberDo(string uid, string pwd, string name, string nickname, string email)
    {
        Userid = uid;
        Passwd = pwd;
        Name = name;
        Nickname = nickname;
        Email = email;
    }
}
