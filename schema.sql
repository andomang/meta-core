-- MetaSearch PostgreSQL 스키마 (라즈베리파이 배포용)
-- 실행: psql -U YOUR_DB_USER -d meta -f schema.sql

-- 회원 테이블
CREATE TABLE IF NOT EXISTS members (
    userid      VARCHAR(50)  PRIMARY KEY,
    passwd      VARCHAR(100) NOT NULL,
    name        VARCHAR(50)  NOT NULL,
    nickname    VARCHAR(50)  NOT NULL,
    email       VARCHAR(100),
    status      VARCHAR(10)  NOT NULL DEFAULT 'true',
    ugrade      INT          NOT NULL DEFAULT 1,
    darkmode    BOOLEAN      NOT NULL DEFAULT false,
    language    VARCHAR(10)  NOT NULL DEFAULT 'ko',
    googlescore DOUBLE PRECISION NOT NULL DEFAULT 0,
    naverscore  DOUBLE PRECISION NOT NULL DEFAULT 0,
    daumscore   DOUBLE PRECISION NOT NULL DEFAULT 0
);

-- 검색 기록 테이블 (7일 자동삭제 대상)
CREATE TABLE IF NOT EXISTS searchhistory (
    no         SERIAL       PRIMARY KEY,
    userid     VARCHAR(50)  NOT NULL,
    query      VARCHAR(500) NOT NULL,
    keyword    VARCHAR(200),
    searchtime TIMESTAMP    NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_searchhistory_userid ON searchhistory(userid);
CREATE INDEX IF NOT EXISTS idx_searchhistory_time   ON searchhistory(searchtime);

-- 인기검색어 캐시 테이블 (TOP10 DB 캐싱)
CREATE TABLE IF NOT EXISTS searchresultcache (
    cacheid     SERIAL        PRIMARY KEY,
    query       VARCHAR(500)  NOT NULL,
    title       VARCHAR(500),
    url         VARCHAR(2000),
    description TEXT,
    source      VARCHAR(50),
    cachedat    TIMESTAMP     NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_searchresultcache_query ON searchresultcache(query);

-- 커뮤니티 게시글 테이블
CREATE TABLE IF NOT EXISTS bbs (
    no          SERIAL        PRIMARY KEY,
    title       VARCHAR(500)  NOT NULL,
    contents    TEXT,
    author      VARCHAR(50)   NOT NULL REFERENCES members(userid),
    uploadtime  TIMESTAMP     NOT NULL DEFAULT NOW(),
    hits        INT           NOT NULL DEFAULT 0,
    filename    VARCHAR(500),
    filesize    INT           NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_bbs_author ON bbs(author);

-- 게시글 좋아요 테이블
CREATE TABLE IF NOT EXISTS bbslike (
    bbsno   INT         NOT NULL REFERENCES bbs(no) ON DELETE CASCADE,
    userid  VARCHAR(50) NOT NULL REFERENCES members(userid) ON DELETE CASCADE,
    PRIMARY KEY (bbsno, userid)
);

-- 게시글 댓글 테이블
CREATE TABLE IF NOT EXISTS bbscomment (
    commentid   SERIAL       PRIMARY KEY,
    bbsno       INT          NOT NULL REFERENCES bbs(no) ON DELETE CASCADE,
    author      VARCHAR(50)  NOT NULL REFERENCES members(userid),
    content     TEXT         NOT NULL,
    createdat   TIMESTAMP    NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_bbscomment_bbsno ON bbscomment(bbsno);

-- 알림 테이블
CREATE TABLE IF NOT EXISTS notifications (
    id          SERIAL       PRIMARY KEY,
    userid      VARCHAR(50)  NOT NULL REFERENCES members(userid) ON DELETE CASCADE,
    actorid     VARCHAR(50)  NOT NULL,
    bbsno       INT          REFERENCES bbs(no) ON DELETE CASCADE,
    commentid   INT          REFERENCES bbscomment(commentid) ON DELETE CASCADE,
    type        VARCHAR(20)  NOT NULL,   -- 'like' | 'comment'
    isread      BOOLEAN      NOT NULL DEFAULT false,
    createdat   TIMESTAMP    NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_notifications_userid ON notifications(userid);
