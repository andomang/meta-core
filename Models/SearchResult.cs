namespace MetaSearch.Models;

/// <summary>
/// 검색 결과 항목 하나를 나타내는 도메인 객체.
/// 네이버/다음/구글 각 엔진 결과 1건의 정보를 담는다.
/// </summary>
public class SearchResult
{
    // 검색 결과 제목
    public string Title { get; set; } = "";
    // 원본 페이지 URL
    public string Url { get; set; } = "";
    // 요약 설명 (스니펫)
    public string Description { get; set; } = "";
    // 검색 엔진 구분자 ("naver", "daum", "google")
    public string Source { get; set; } = "";
    // 대표 이미지 URL (카카오 API 등에서 제공)
    public string ImageUrl { get; set; } = "";
    // 관련도 점수 (RRF 가중치 합산, double)
    public double Score { get; set; }
}
