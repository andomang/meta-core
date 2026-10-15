#!/bin/bash
# ============================================================
# MetaSearch 라즈베리파이 배포 스크립트
# 대상: Linux-arm64 (Raspberry Pi 4/5)
# 사전 요구: .NET 8 SDK, PostgreSQL, Nginx
# ============================================================

# 배포 경로
DEPLOY_DIR="/home/pi/metasearch"
SERVICE_NAME="metasearch"

echo "=== MetaSearch 배포 시작 ==="

# 1. 빌드 (linux-arm64 자체포함 배포)
echo "[1/5] 빌드 중..."
dotnet publish -c Release -r linux-arm64 --self-contained -o ./publish

# 2. 배포 폴더 생성 및 복사
echo "[2/5] 배포 파일 복사..."
sudo mkdir -p $DEPLOY_DIR
sudo cp -r ./publish/* $DEPLOY_DIR/
sudo chown -R pi:pi $DEPLOY_DIR
chmod +x $DEPLOY_DIR/MetaSearch

# 3. systemd 서비스 등록
echo "[3/5] systemd 서비스 설정..."
sudo tee /etc/systemd/system/${SERVICE_NAME}.service > /dev/null <<EOF
[Unit]
Description=MetaSearch Web Application
After=network.target postgresql.service

[Service]
WorkingDirectory=${DEPLOY_DIR}
ExecStart=${DEPLOY_DIR}/MetaSearch
Restart=always
RestartSec=10
User=pi
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable ${SERVICE_NAME}

# 4. Nginx 설정
echo "[4/5] Nginx 설정..."
sudo cp nginx.conf /etc/nginx/sites-available/${SERVICE_NAME}
sudo ln -sf /etc/nginx/sites-available/${SERVICE_NAME} /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx

# 5. PostgreSQL 스키마 초기화 (최초 1회만 실행)
echo "[5/5] DB 스키마 확인..."
echo "  → 최초 배포 시: psql -U YOUR_DB_USER -d meta -f init.sql"

echo ""
echo "=== 배포 완료 ==="
echo "시작: sudo systemctl start ${SERVICE_NAME}"
echo "로그: sudo journalctl -u ${SERVICE_NAME} -f"
echo "상태: sudo systemctl status ${SERVICE_NAME}"
