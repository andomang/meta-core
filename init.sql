-- ============================================================
-- MetaSearch PostgreSQL 스키마
-- 라즈베리파이용 .NET 8 포팅 전용
-- 실행: psql -U YOUR_DB_USER -d meta -f init.sql
-- ============================================================

-- 회원 테이블
CREATE TABLE IF NOT EXISTS members (
    userid      VARCHAR(50)  PRIMARY KEY,          -- 로그인 ID
    passwd      VARCHAR(100) NOT NULL,             -- MD5 해시 비밀번호
    name        VARCHAR(50)  NOT NULL,             -- 실명
    nickname    VARCHAR(50)  NOT NULL,             -- 닉네임
    email       VARCHAR(100) DEFAULT '',           -- 이메일
    status      VARCHAR(20)  DEFAULT 'active',     -- 계정 상태
    ugrade      VARCHAR(20)  DEFAULT 'normal',     -- 등급 (normal/admin)
    darkmode    VARCHAR(10)  DEFAULT 'light',      -- 다크모드 설정
    language    VARCHAR(10)  DEFAULT 'ko',         -- 언어 설정
    googlescore INT          DEFAULT 100,          -- 개인화 구글 가중치
    naverscore  INT          DEFAULT 100,          -- 개인화 네이버 가중치
    daumscore   INT          DEFAULT 100           -- 개인화 다음 가중치
);

-- 게시판 테이블
CREATE TABLE IF NOT EXISTS bbs (
    no          SERIAL       PRIMARY KEY,          -- 게시글 번호 (자동증가)
    title       VARCHAR(200) NOT NULL,             -- 제목
    contents    TEXT         DEFAULT '',            -- 본문 (HTML 허용)
    author      VARCHAR(50)  NOT NULL,             -- 작성자 userid
    uploadtime  TIMESTAMP    DEFAULT NOW(),        -- 작성일시
    hits        INT          DEFAULT 0,            -- 조회수
    filename    VARCHAR(200) DEFAULT '',           -- 첨부파일명
    filesize    INT          DEFAULT 0             -- 첨부파일 크기(바이트)
);

-- 게시글 좋아요 테이블
CREATE TABLE IF NOT EXISTS bbslike (
    bbsno       INT          NOT NULL,             -- 게시글 번호
    userid      VARCHAR(50)  NOT NULL,             -- 좋아요 누른 사용자
    PRIMARY KEY (bbsno, userid),
    FOREIGN KEY (bbsno) REFERENCES bbs(no) ON DELETE CASCADE
);

-- 게시글 댓글 테이블
CREATE TABLE IF NOT EXISTS bbscomment (
    commentid   SERIAL       PRIMARY KEY,          -- 댓글 ID (자동증가)
    bbsno       INT          NOT NULL,             -- 게시글 번호
    author      VARCHAR(50)  NOT NULL,             -- 댓글 작성자 userid
    content     TEXT         NOT NULL,             -- 댓글 내용
    createdat   TIMESTAMP    DEFAULT NOW(),        -- 작성일시
    FOREIGN KEY (bbsno) REFERENCES bbs(no) ON DELETE CASCADE
);

-- 알림 테이블
CREATE TABLE IF NOT EXISTS notifications (
    notifid     SERIAL       PRIMARY KEY,          -- 알림 ID (자동증가)
    userid      VARCHAR(50)  NOT NULL,             -- 알림 받는 사용자
    actorid     VARCHAR(50)  NOT NULL,             -- 알림 유발 사용자
    bbsno       INT          DEFAULT 0,            -- 관련 게시글 번호
    commentid   INT          DEFAULT 0,            -- 관련 댓글 ID
    type        VARCHAR(20)  DEFAULT 'comment',    -- 알림 유형 (comment/like)
    isread      BOOLEAN      DEFAULT FALSE,        -- 읽음 여부
    createdat   TIMESTAMP    DEFAULT NOW()         -- 생성일시
);

-- 검색 이력 테이블 (7일 자동 삭제 정책)
CREATE TABLE IF NOT EXISTS searchhistory (
    id          SERIAL       PRIMARY KEY,          -- 이력 ID
    userid      VARCHAR(50)  NOT NULL,             -- 검색한 사용자
    query       VARCHAR(200) NOT NULL,             -- 검색어
    keyword     VARCHAR(50)  DEFAULT '일반',        -- AI 분류 키워드
    searchtime  TIMESTAMP    DEFAULT NOW()         -- 검색 시각
);

-- 검색 결과 캐시 테이블 (7일 유효)
CREATE TABLE IF NOT EXISTS searchresultcache (
    cacheid     SERIAL       PRIMARY KEY,          -- 캐시 ID
    query       VARCHAR(200) NOT NULL,             -- 검색어
    title       VARCHAR(500) DEFAULT '',           -- 결과 제목
    url         VARCHAR(1000) DEFAULT '',          -- 결과 URL
    description TEXT         DEFAULT '',           -- 결과 설명
    source      VARCHAR(50)  DEFAULT '',           -- 출처 (google/naver/daum)
    cachedat    TIMESTAMP    DEFAULT NOW()         -- 캐시 저장 시각
);

-- ── 인덱스 ──

-- 검색 이력: 사용자별 최근 검색어 조회용
CREATE INDEX IF NOT EXISTS idx_searchhistory_userid ON searchhistory(userid, searchtime DESC);

-- 검색 결과 캐시: 검색어+날짜 기반 캐시 히트용
CREATE INDEX IF NOT EXISTS idx_searchresultcache_query ON searchresultcache(query, cachedat DESC);

-- 알림: 사용자별 미읽음 알림 조회용
CREATE INDEX IF NOT EXISTS idx_notifications_userid ON notifications(userid, isread, createdat DESC);

-- 게시판: 최신글 목록 조회용
CREATE INDEX IF NOT EXISTS idx_bbs_uploadtime ON bbs(uploadtime DESC);

-- 댓글: 게시글별 댓글 조회용
CREATE INDEX IF NOT EXISTS idx_bbscomment_bbsno ON bbscomment(bbsno, createdat);
