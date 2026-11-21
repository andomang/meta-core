# Meta Search · domang.org 서버

[domang.org](https://domang.org) 전체를 서비스하는 ASP.NET Core 8 웹 서버입니다. 포트폴리오, **Meta Search** 통합 검색엔진, **Cloud Drive**, 포켓몬스터 아스테라 게임 API를 한 서버에서 운영합니다.

## Meta Search

구글·네이버·다음·Brave 네 검색엔진의 결과를 **RRF(Reciprocal Rank Fusion)** 로 합쳐 한 번에 보여 주는 메타 검색엔진입니다.

- 엔진별 크롤러를 병렬로 호출하고 순위를 합쳐 재정렬
- AI 추천 검색, 자동완성, 인기 검색어 TOP10, 실시간 트렌드
- 검색 기록(7일 지나면 자동 삭제), 북마크, 커뮤니티, 다크 모드·한/영 전환
- [성능 평가 리포트](https://domang.org/EvalReport): 고정 테스트셋 60개로 단일 엔진과 RRF 통합 결과를 비교

## Cloud Drive

파일 업로드·다운로드, 폴더 관리, 즐겨찾기, 휴지통, 검색을 지원하는 개인 드라이브입니다. 비밀번호는 PBKDF2-SHA256으로 저장합니다.

## 포켓몬스터 아스테라 서버 기능

- 회원 계정으로 게임 기록·설정 저장 (`/api/astera/*`)
- 관리자 페이지 `/pokeadmin`: 회원별 진행·돈·파티·박스·가방 조회와 수정
- "이상한 소포": 관리자가 만든 포켓몬·도구 선물을 포켓몬센터에서 받기 (계정당 1번)
- 게임 스크립트를 미리 압축(brotli)한 묶음으로 서빙, 버전 붙은 주소로 1년 캐시

## 기술 스택

| 구분 | 사용 |
|---|---|
| 서버 | ASP.NET Core 8 (Razor Pages + Minimal API) |
| DB | PostgreSQL + Dapper (Npgsql) |
| 인증 | 세션, BCrypt(회원) / PBKDF2(드라이브), 로그인 시도 제한 |
| 메일 | Brevo API (가입·계정 찾기 인증 코드) |
| 화면 | Tailwind CSS, Vanilla JS |
| 운영 | Raspberry Pi (linux-arm64), systemd, Cloudflare |

## 실행 방법

```bash
# 1. 설정 파일 만들기 (API 키·DB 연결 문자열 입력)
cp appsettings.example.json appsettings.json

# 2. 실행 (테이블은 처음 실행할 때 자동 생성)
dotnet run
```

라즈베리파이 배포는 저장소 루트의 `rasp/deploy.bat`이 처리합니다(linux-arm64 self-contained 빌드 → 바뀐 파일만 업로드 → 서비스 재시작).

## 폴더

```
Pages/       Razor 페이지 (포트폴리오, 검색, 커뮤니티, 드라이브, 관리자, 소개 페이지)
Services/    검색엔진 크롤러, DB 접근(MemberDao·SearchDao), 드라이브, 메일, AI API
Program.cs   서비스 등록, 보안 헤더(CSP), 압축, 정적 파일, Minimal API
wwwroot/     정적 파일 (sitemap.xml, robots.txt, 이미지)
```
