# 라즈베리파이 배포 가이드

## 1. 라즈베리파이에 .NET 8 설치

```bash
# ARM64 런타임 설치 스크립트
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0 --runtime aspnetcore

# 환경변수 추가 (~/.bashrc 또는 ~/.profile에 추가)
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$HOME/.dotnet
```

## 2. PostgreSQL 설치 및 설정

```bash
sudo apt update
sudo apt install -y postgresql postgresql-contrib

# PostgreSQL 서비스 시작
sudo systemctl enable postgresql
sudo systemctl start postgresql

# DB 및 사용자 생성 (비밀번호는 직접 설정)
sudo -u postgres psql <<'SQL'
CREATE DATABASE meta;
CREATE USER metauser WITH ENCRYPTED PASSWORD 'your_password_here';
GRANT ALL PRIVILEGES ON DATABASE meta TO metauser;
\c meta
GRANT ALL ON SCHEMA public TO metauser;
SQL
```

## 3. 스키마 생성

```bash
# schema.sql을 라즈베리파이로 복사 후
psql -U metauser -d meta -f schema.sql
```

## 4. 라즈베리파이에 삼바(Samba) 설정

```bash
sudo apt install -y samba

sudo nano /etc/samba/smb.conf
```

파일 끝에 추가:
```ini
[metasearch]
   path = /home/pi/metasearch
   browseable = yes
   writable = yes
   valid users = pi
   create mask = 0755
```

```bash
# 삼바 비밀번호 설정 (pi 계정)
sudo smbpasswd -a pi

sudo systemctl restart smbd
```

## 5. Windows에서 발행 후 삼바로 복사

```powershell
cd C:\2026\meta\rasp\MetaSearch

# linux-arm64 발행
dotnet publish -c Release -r linux-arm64 --self-contained true -o ./publish

# 삼바 공유 폴더로 복사 (탐색기에서 \\라즈베리파이IP\metasearch 접속 후 붙여넣기도 가능)
# 또는 PowerShell:
Copy-Item -Path "C:\2026\meta\rasp\MetaSearch\publish\*" -Destination "\\192.168.x.x\metasearch\" -Recurse -Force
Copy-Item -Path "C:\2026\meta\rasp\MetaSearch\schema.sql" -Destination "\\192.168.x.x\metasearch\"
```

> **탐색기 방법:** `\\192.168.x.x\metasearch` 주소창에 입력 → pi 계정으로 로그인 → publish 폴더 내용물 전부 복사·붙여넣기

## 6. appsettings.json 연결 문자열 수정

삼바 공유 폴더에서 직접 수정하거나, 라즈베리파이에서:
```bash
nano /home/pi/metasearch/appsettings.json
```

`DefaultConnection` 값을:
```
Host=localhost;Port=5432;Database=meta;Username=metauser;Password=your_password_here
```

## 7. systemd 서비스 등록

```bash
sudo nano /etc/systemd/system/metasearch.service
```

```ini
[Unit]
Description=MetaSearch ASP.NET Core App
After=network.target postgresql.service

[Service]
WorkingDirectory=/home/pi/metasearch
ExecStart=/home/pi/metasearch/MetaSearch
Restart=always
RestartSec=10
User=pi
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000
KillSignal=SIGINT

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable metasearch
sudo systemctl start metasearch

# 로그 확인
sudo journalctl -u metasearch -f
```

## 8. nginx 리버스 프록시 (선택)

```bash
sudo apt install -y nginx
sudo nano /etc/nginx/sites-available/metasearch
```

```nginx
server {
    listen 80;
    server_name _;  # 또는 라즈베리파이 IP/도메인

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_cache_bypass $http_upgrade;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/metasearch /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx
```

## 9. 실행 권한 부여

```bash
chmod +x /home/pi/metasearch/MetaSearch
```

## 빠른 재배포 스크립트 (Windows PowerShell)

```powershell
# redeploy.ps1  (192.168.x.x 를 실제 라즈베리파이 IP로 수정)
dotnet publish C:\2026\meta\rasp\MetaSearch -c Release -r linux-arm64 --self-contained true -o C:\2026\meta\rasp\MetaSearch\publish
Copy-Item -Path "C:\2026\meta\rasp\MetaSearch\publish\*" -Destination "\\192.168.x.x\metasearch\" -Recurse -Force

# 서비스 재시작은 라즈베리파이에서 직접:
#   sudo systemctl restart metasearch
```
