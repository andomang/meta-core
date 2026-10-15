namespace MetaSearch.Services;

/// <summary>
/// 키워드별 검색 엔진 우선순위 점수 정의.
/// GetScores(keyword) → [구글점수, 네이버점수, 다음점수] 반환.
/// 점수가 높을수록 해당 엔진 결과를 상위에 배치한다.
/// </summary>
public static class SearchScoreTable
{
    // 키워드 → [구글, 네이버, 다음] 점수 매핑
    private static readonly Dictionary<string, int[]> _scores = new()
    {
        // 해외 계열: 구글 최우선
        { "해외뉴스", new[] { 3, 2, 1 } },
        { "해외맛집", new[] { 3, 2, 1 } },
        { "해외장소", new[] { 3, 2, 1 } },
        { "IT기술",   new[] { 3, 2, 1 } },

        // 국내 계열: 네이버 최우선
        { "국내뉴스", new[] { 1, 3, 2 } },
        { "국내맛집", new[] { 1, 3, 2 } },
        { "국내장소", new[] { 1, 3, 2 } },
        { "쇼핑",     new[] { 1, 3, 2 } },

        // 전문 영역: 네이버 커뮤니티 강점
        { "게임",     new[] { 2, 3, 1 } },
        { "차량",     new[] { 2, 3, 1 } },
        { "의료건강", new[] { 2, 3, 1 } },
        { "스포츠",   new[] { 2, 3, 1 } },

        // 일반: 한국 서비스이므로 네이버 약간 우선
        { "일반",     new[] { 1, 2, 1 } },
    };

    /// <summary>키워드에 해당하는 [구글, 네이버, 다음] 점수 배열 반환</summary>
    public static int[] GetScores(string keyword)
    {
        if (!string.IsNullOrEmpty(keyword) && _scores.TryGetValue(keyword, out var s))
            return s;
        return new[] { 1, 1, 1 };
    }
}
