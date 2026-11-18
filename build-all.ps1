# LANVC Master Build & Package Script (.NET 10 x64)

$ErrorActionPreference = "Stop"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "          LANVC Master Build Script          " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 1. Restore & Build Solution
Write-Host "[1/5] Сборка решения LANVC.slnx..." -ForegroundColor Yellow
dotnet restore LANVC.slnx
dotnet build LANVC.slnx -c Release --no-restore

# 2. Run Test Suite
Write-Host "[2/5] Запуск полного набора тестов..." -ForegroundColor Yellow
dotnet test tests/LANVC.Tests/LANVC.Tests.csproj -c Release --no-build

# 3. Publish Client
Write-Host "[3/5] Публикация LANVC.Client..." -ForegroundColor Yellow
dotnet publish src/LANVC.Client/LANVC.Client.csproj -c Release -r win-x64 --self-contained false -o dist/client
Copy-Item wintun_sdk/wintun/bin/amd64/wintun.dll dist/client/ -Force

# 4. Publish Server & Admin CLI
Write-Host "[4/5] Публикация LANVC.Server и LANVC.Admin..." -ForegroundColor Yellow
dotnet publish src/LANVC.Server/LANVC.Server.csproj -c Release -r win-x64 --self-contained false -o dist/server
dotnet publish src/LANVC.Admin/LANVC.Admin.csproj -c Release -r win-x64 --self-contained false -o dist/server

# 5. Create Distribution Archives
Write-Host "[5/5] Упаковка дистрибутивов в ZIP..." -ForegroundColor Yellow
Compress-Archive -Path dist/client/* -DestinationPath dist/LANVC_Client_v1.0.0.zip -Force
Compress-Archive -Path dist/server/* -DestinationPath dist/LANVC_Server_v1.0.0.zip -Force

Write-Host "=============================================" -ForegroundColor Green
Write-Host "  Сборка успешно завершена! Дистрибутивы:    " -ForegroundColor Green
Write-Host "  - dist/LANVC_Client_v1.0.0.zip             " -ForegroundColor Green
Write-Host "  - dist/LANVC_Server_v1.0.0.zip             " -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
