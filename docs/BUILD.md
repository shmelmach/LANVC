# LANVC — Руководство по сборке проекта (BUILD.md)

## 1. Необходимые инструменты разработчика

- **SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/) (x64)
- **ОС:** Windows 10/11 x64
- **Git**

---

## 2. Структура решения (Solution)

```
LANVC/
├── LANVC.sln
├── src/
│   ├── LANVC.Shared/          # Бинарный протокол, DTO, заголовки, структуры
│   ├── LANVC.Core/            # IP-маршрутизация, Broadcast/Multicast ретранслятор, IP-пул
│   ├── LANVC.Security/        # Argon2id, TLS 1.3 сертификаты, Windows Credential Manager
│   ├── LANVC.Adapter/         # P/Invoke Wintun, управление IP и маршрутами
│   ├── LANVC.Transport/       # QUIC (MsQuic) и WebSocket транспорты, Failover Manager
│   ├── LANVC.Client/          # WPF GUI (современный темный интерфейс, ViewModels)
│   ├── LANVC.Server/          # Серверная часть, Windows Service, Hub-and-Spoke
│   └── LANVC.Admin/           # Консоль администратора сервера (CLI)
├── tests/
│   └── LANVC.Tests/           # Модульные и интеграционные тесты
├── docs/                      # Документация
└── installer/                 # Скрипты инсталляции и сервиса
```

---

## 3. Сборка из командной строки

### Восстановление пакетов и сборка решения:
```powershell
dotnet restore LANVC.sln
dotnet build LANVC.sln -c Release
```

### Запуск тестов:
```powershell
dotnet test tests/LANVC.Tests/LANVC.Tests.csproj -c Release
```

### Публикация готовых дистрибутивов (Single-File Self-Contained):

**Сборка клиента:**
```powershell
dotnet publish src/LANVC.Client/LANVC.Client.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/client
Copy-Item wintun_sdk/wintun/bin/amd64/wintun.dll dist/client/
```

**Сборка сервера:**
```powershell
dotnet publish src/LANVC.Server/LANVC.Server.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/server
```

**Сборка CLI админки:**
```powershell
dotnet publish src/LANVC.Admin/LANVC.Admin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/server
```
