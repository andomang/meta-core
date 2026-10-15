namespace MetaSearch.Services;

/// <summary>
/// 다국어(한국어/영어) 텍스트 제공 서비스.
/// Session["Lang"] 값에 따라 한국어 또는 영어 텍스트를 반환한다.
/// ASP.NET Core에서는 IHttpContextAccessor를 통해 세션에 접근한다.
/// </summary>
public class Lang
{
    private readonly IHttpContextAccessor _ctx;

    public Lang(IHttpContextAccessor ctx)
    {
        _ctx = ctx;
    }

    /// <summary>현재 세션의 언어가 영어인지 확인</summary>
    private bool IsEn
    {
        get
        {
            var session = _ctx.HttpContext?.Session;
            if (session == null) return false;
            return session.GetString("Lang") == "en";
        }
    }

    /// <summary>
    /// 키에 해당하는 현재 언어의 UI 텍스트를 반환한다.
    /// 미등록 키는 키 자체를 반환한다.
    /// </summary>
    public string Get(string key)
    {
        bool en = IsEn;
        return key switch
        {
            // ── Site.Master (네비게이션) ──
            "nav.community"   => en ? "Community"   : "커뮤니티",
            "nav.settings"    => en ? "Settings"    : "설정",
            "nav.mypage"      => en ? "My Page"     : "마이페이지",
            "nav.login"       => en ? "Login"       : "로그인",
            "nav.logout"      => en ? "Logout"      : "로그아웃",
            "nav.greeting"    => en ? ""            : "님",

            // 로그인 모달
            "login.title"     => en ? "Login"       : "로그인",
            "login.id"        => en ? "Username"    : "아이디",
            "login.pw"        => en ? "Password"    : "비밀번호",
            "login.btn"       => en ? "Sign In"     : "로그인",
            "login.noAccount" => en ? "Don't have an account?" : "계정이 없으신가요?",
            "login.register"  => en ? "Sign up"     : "회원가입",
            "login.close"     => en ? "Close"       : "닫기",

            // ── Default (홈) ──
            "home.subtitle"     => en ? "Search everything"       : "모든 정보를 검색하세요",
            "home.placeholder"  => en ? "Search anything..."      : "검색어를 입력하세요",
            "home.community"    => en ? "Community"               : "자유게시판",
            "home.commSub"      => en ? "Share and discuss"       : "함께 이야기 나누는 공간",
            "home.settings"     => en ? "Settings"                : "개인 설정",
            "home.settSub"      => en ? "Dark mode & preferences" : "다크모드 및 환경 설정",

            // ── SearchResults ──
            "search.results"      => en ? "Search results"      : "검색 결과 리스트입니다.",
            "search.related"      => en ? "Related searches"    : "관련 검색어",
            "search.popular"      => en ? "Popular searches"    : "인기 검색어",
            "search.recent"       => en ? "Recent searches"     : "최근 검색어",
            "search.noHistory"    => en ? "No search history."  : "검색 기록이 없습니다.",
            "search.placeholder"  => en ? "Search anything..."  : "검색어를 입력하세요",
            "search.noResults"    => en ? "No results found."   : "검색 결과가 없습니다.",
            "search.noResultsSub" => en ? "Try a different keyword." : "다른 검색어를 입력해 보세요.",
            "search.loading"      => en ? "Searching..."        : "검색 중...",
            "search.times"        => en ? "times"               : "회",
            // sr.* 단축 키 (SearchResults.cshtml에서 사용)
            "sr.all"              => en ? "All"                 : "전체",
            "sr.noResults"        => en ? "No results found."   : "검색 결과가 없습니다.",
            "sr.related"          => en ? "Related searches"    : "관련 검색어",

            // ── Community ──
            "comm.title"      => en ? "Community"   : "커뮤니티 광장",
            "comm.write"      => en ? "Write"       : "글쓰기",
            "comm.no"         => en ? "No."         : "번호",
            "comm.colTitle"   => en ? "Title"       : "제목",
            "comm.titleCol"   => en ? "Title"       : "제목",
            "comm.colAuthor"  => en ? "Author"      : "작성자",
            "comm.author"     => en ? "Author"      : "작성자",
            "comm.colDate"    => en ? "Date"        : "날짜",
            "comm.date"       => en ? "Date"        : "날짜",
            "comm.hits"       => en ? "Views"       : "조회수",
            "comm.noPosts"    => en ? "No posts yet." : "게시글이 없습니다.",
            "comm.prev"       => en ? "Prev"        : "이전",
            "comm.next"       => en ? "Next"        : "다음",
            "comm.empty"      => en ? "No posts yet." : "게시글이 없습니다.",

            // ── CommunityView ──
            "view.board"      => en ? "Free Board"   : "자유게시판",
            "view.download"   => en ? "Download"     : "다운로드",
            "view.back"       => en ? "Back to list" : "목록으로",
            "view.delete"     => en ? "Delete"       : "삭제",
            "view.edit"       => en ? "Edit"         : "수정",
            "view.confirmDel" => en ? "Are you sure you want to delete this post?" : "정말 삭제하시겠습니까?",

            // ── CommunityEdit ──
            "edit.title"      => en ? "Edit Post"    : "게시글 수정",
            "edit.titleLbl"   => en ? "Title"        : "제목",
            "edit.content"    => en ? "Content"      : "내용",
            "edit.cancel"     => en ? "Cancel"       : "취소",
            "edit.submit"     => en ? "Save"         : "저장하기",

            // ── CommunityWrite ──
            "write.title"     => en ? "New Post"      : "새 게시글 작성",
            "write.titleLbl"  => en ? "Title"         : "제목",
            "write.titlePh"   => en ? "Enter title"   : "제목을 입력하세요",
            "write.content"   => en ? "Content"       : "내용",
            "write.contentPh" => en ? "Enter content" : "내용을 입력하세요",
            "write.file"      => en ? "Attachment"    : "파일 첨부",
            "write.cancel"    => en ? "Cancel"        : "취소",
            "write.submit"    => en ? "Post"          : "등록하기",

            // ── Register ──
            "reg.title"     => en ? "Sign Up"              : "회원가입",
            "reg.id"        => en ? "Username"             : "아이디",
            "reg.idPh"      => en ? "6~15 alphanumeric"    : "6~15자 영문/숫자",
            "reg.pw"        => en ? "Password"             : "비밀번호",
            "reg.pwPh"      => en ? "Enter password"       : "비밀번호를 입력하세요",
            "reg.name"      => en ? "Name"                 : "이름",
            "reg.nick"      => en ? "Nickname"             : "닉네임",
            "reg.email"     => en ? "Email"                : "이메일",
            "reg.cancel"    => en ? "Cancel"               : "취소",
            "reg.submit"    => en ? "Create Account"       : "가입 완료",
            "reg.fillAll"   => en ? "Please fill in all fields." : "모든 항목을 입력해주세요.",
            "reg.duplicate" => en ? "This username is already taken." : "이미 사용 중인 아이디입니다.",

            // ── Settings ──
            "set.title"           => en ? "Settings"            : "설정",
            "set.general"         => en ? "General"             : "일반",
            "set.privacy"         => en ? "My Info"             : "내 정보",
            "set.tabGeneral"      => en ? "General"             : "일반",
            "set.tabPrivacy"      => en ? "My Info"             : "내 정보",
            "set.theme"           => en ? "Display Theme"       : "화면 테마",
            "set.themeDesc"       => en ? "Toggle current theme." : "현재 테마 상태를 전환합니다.",
            "set.themeSub"        => en ? "Toggle current theme." : "현재 테마 상태를 전환합니다.",
            "set.themeBtn"        => en ? "Toggle"              : "테마 변경",
            "set.themeOn"         => en ? "On"                  : "On",
            "set.themeOff"        => en ? "Off"                 : "Off",
            "set.language"        => en ? "Language"            : "언어 설정",
            "set.lang"            => en ? "Language"            : "언어 설정",
            "set.langSub"         => en ? "Select service language." : "서비스 언어를 선택합니다.",
            "set.profile"         => en ? "Edit Profile"        : "내 정보 수정",
            "set.myInfo"          => en ? "Edit Profile"        : "내 정보 수정",
            "set.name"            => en ? "Name"                : "이름",
            "set.nick"            => en ? "Nickname"            : "닉네임",
            "set.email"           => en ? "Email"               : "이메일",
            "set.save"            => en ? "Save"                : "저장",
            "set.changePw"        => en ? "Change Password"     : "비밀번호 변경",
            "set.updateBtn"       => en ? "Update"              : "정보 업데이트",
            "set.engineScore"     => en ? "Search Preferences"  : "내 검색 성향",
            "set.engineScores"    => en ? "Search Preferences"  : "내 검색 성향",
            "set.resetScores"     => en ? "Reset"               : "성향 초기화",
            "set.engineScoreDesc" => en ? "Scores accumulate as you search. Higher score = shown first." : "검색할수록 점수가 쌓이며, 높은 점수의 엔진이 검색 결과 상단에 더 많이 노출됩니다.",
            "set.recentSearch"    => en ? "Recent Searches"     : "최근 검색어",
            "set.myPosts"         => en ? "My Posts"            : "내 게시글",
            "set.viewAll"         => en ? "View all"            : "전체 보기",
            "set.noSearch"        => en ? "No search history."  : "검색 기록이 없습니다.",
            "set.noPosts"         => en ? "No posts yet."       : "작성한 게시글이 없습니다.",
            "set.stats"           => en ? "My Search Stats"     : "내 검색 통계",
            "set.totalSearch"     => en ? "Total Searches"      : "총 검색 횟수",
            "set.topKeyword"      => en ? "Top Keyword"         : "가장 많이 검색한 키워드",
            "set.totalClick"      => en ? "Total Clicks"        : "총 클릭 횟수",
            "set.topCategory"     => en ? "Top Category"        : "가장 많은 카테고리",
            "set.top5"            => en ? "Top 5 Keywords"      : "자주 검색한 키워드 TOP 5",
            "set.deleteHistory"   => en ? "Delete Search History" : "검색 기록 삭제",
            "set.delSearch"       => en ? "Delete Search History" : "검색 기록 삭제",
            "set.delSearchSub"    => en ? "Delete search records by period." : "특정 기간의 검색 기록을 삭제합니다.",
            "set.delBtn"          => en ? "Delete"              : "기록 삭제",
            "set.withdraw"        => en ? "Delete Account"      : "회원 탈퇴",
            "set.withdrawDesc"    => en ? "Your account and all data will be permanently deleted." : "계정과 모든 데이터가 영구적으로 삭제됩니다.",
            "set.withdrawSub"     => en ? "Your account and all data will be permanently deleted." : "계정과 모든 데이터가 영구적으로 삭제됩니다.",
            "set.withdrawBtn"     => en ? "Delete Account"      : "탈퇴하기",
            "set.withdrawConfirm" => en ? "Confirm Account Deletion" : "정말 탈퇴하시겠습니까?",
            "set.withdrawWarn"    => en ? "Enter your password to confirm. All data will be permanently deleted." : "비밀번호를 입력하면 계정과 모든 데이터가 영구 삭제됩니다.",
            "set.delModal1h"      => en ? "Last 1 hour"         : "최근 1시간",
            "set.delModal12h"     => en ? "Last 12 hours"       : "최근 12시간",
            "set.delModal1d"      => en ? "Last 1 day"          : "최근 1일",
            "set.delModal7d"      => en ? "Last 7 days"         : "최근 7일",
            "set.delModal30d"     => en ? "Last 30 days"        : "최근 30일",
            "set.delModalAll"     => en ? "Delete All"          : "전체 삭제",
            "set.cancel"          => en ? "Cancel"              : "취소",
            "set.confirm"         => en ? "Confirm"             : "확인",
            "set.oldPw"           => en ? "Current Password"    : "현재 비밀번호",
            "set.newPw"           => en ? "New Password"        : "새 비밀번호",
            "set.pwModal"         => en ? "Change Password"     : "비밀번호 변경",
            "set.pwCur"           => en ? "Current Password"    : "현재 비밀번호",
            "set.pwNew"           => en ? "New Password"        : "새 비밀번호",
            "set.pwConfirm"       => en ? "Confirm Password"    : "새 비밀번호 확인",
            "set.pwBtn"           => en ? "Change"              : "변경하기",
            "set.pwChanged"       => en ? "Password changed successfully." : "비밀번호가 변경되었습니다.",
            "set.wrongPw"         => en ? "Current password is incorrect." : "현재 비밀번호가 맞지 않습니다.",
            "set.resetConfirm"    => en ? "Reset your search preferences?" : "검색 성향을 초기화하시겠습니까?",
            "set.wdModal"         => en ? "Delete Account"      : "회원 탈퇴",
            "set.wdDesc"          => en ? "Enter your password to permanently delete your account and all data." : "비밀번호를 입력하면 계정과 모든 데이터가 영구 삭제됩니다.",
            "set.wdPh"            => en ? "Confirm password"    : "비밀번호 확인",
            "set.wdBtn"           => en ? "Confirm Delete"      : "탈퇴 확인",

            // ── 댓글 ──
            "comm.comments"      => en ? "Comments"               : "댓글",
            "comm.noComments"    => en ? "No comments yet."       : "첫 댓글을 작성해보세요.",
            "comm.commentPh"     => en ? "Write a comment..."     : "댓글을 입력하세요...",
            "comm.commentBtn"    => en ? "Post"                   : "댓글 작성",
            "comm.delComment"    => en ? "Delete"                 : "삭제",
            "comm.confirmDelCmt" => en ? "Delete this comment?"   : "댓글을 삭제하시겠습니까?",
            "comm.like"          => en ? "Like"                   : "좋아요",
            "comm.unlike"        => en ? "Unlike"                 : "좋아요 취소",
            "comm.searchPh"      => en ? "Search posts..."        : "게시물 검색...",
            "comm.searchBtn"     => en ? "Search"                 : "검색",
            "comm.searchReset"   => en ? "All posts"              : "전체보기",

            // ── 알림 ──
            "notif.title"        => en ? "Notifications"          : "알림",
            "notif.noNotif"      => en ? "No notifications."      : "알림이 없습니다.",
            "notif.markRead"     => en ? "Mark all read"          : "모두 읽음",
            "notif.comment"      => en ? "commented on your post" : "님이 내 게시글에 댓글을 달았습니다",
            "notif.like"         => en ? "liked your post"        : "님이 내 게시글에 좋아요를 눌렀습니다",

            // 미등록 키 → 키 자체 반환
            _ => key
        };
    }
}
