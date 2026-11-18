# LANVC — Руководство по установке и развёртыванию (INSTALL.md)

## 1. Системные требования

### Клиент (LANVC Client):
- **ОС:** Windows 10 x64 (версия 1903+) или Windows 11 x64.
- **Права администратора:** требуются только при первом запуске / установке драйвера виртуального адаптера Wintun.
- **Сеть:** подключение к сети Интернет (Ethernet или Wi-Fi).

### Сервер (LANVC Server):
- **ОС:** Windows 11 Pro x64 (или Windows Server 2022/2025).
- **Процессор:** Intel Core i3-12100F (или аналогичный с 4 ядрами).
- **ОЗУ:** от 4 ГБ (16 ГБ с запасом).
- **Сетевое подключение:** Белый публичный статический IPv4-адрес.
- **Открытые порты брандмауэра:**
  - `443/UDP` — для основного транспорта QUIC.
  - `443/TCP` — для резервного транспорта WebSocket / HTTPS.

---

## 2. Установка сервера (Windows 11 Pro)

### Способ 1: Автоматическая установка как Windows Service (Рекомендуемый)

1. Распакуйте архив `LANVC_Server_Release.zip` в каталог `C:\Program Files\LANVC_Server\` (или любую удобную папку).
2. Запустите консоль PowerShell от имени Администратора:
   ```powershell
   cd "C:\Program Files\LANVC_Server"
   .\install-service.ps1
   ```
   Скрипт автоматически:
   - Создаст и настроит Windows Service `LANVC.Server` с типом запуска "Автоматически".
   - Добавит разрешающие правила в Windows Defender Firewall для портов 443 TCP и 443 UDP.
   - Запустит службу.

### Способ 2: Ручной запуск через LANVC Admin CLI

Для тестирования или управления без фоновой службы:
```powershell
.\LANVC.Admin.exe set-password "MySecretLanPassword123"
.\LANVC.Server.exe --console
```

---

## 3. Установка клиента (Windows 10 / 11)

1. Распакуйте архив `LANVC_Client.zip` или запустите установщик `LANVC_Setup.exe`.
2. Библиотека `wintun.dll` уже встроена в комплект поставки и располагается рядом с исполняемым файлом `LANVC.Client.exe`.
3. Запустите `LANVC.Client.exe` (при первом запуске Windows запросит подтверждение UAC для инициализации сетевого адаптера).
4. Введите IP-адрес сервера (например, `203.0.113.42`), порт (`443`) и пароль сети.
5. Нажмите **«Подключиться»**.

---

## 4. Настройка Windows Firewall на сервере

Если вы настраиваете брандмауэр вручную, выполните в PowerShell (Admin):

```powershell
# Разрешить входящий QUIC (UDP 443)
New-NetFirewallRule -DisplayName "LANVC Server QUIC (UDP 443)" -Direction Inbound -LocalPort 443 -Protocol UDP -Action Allow

# Разрешить входящий WebSocket (TCP 443)
New-NetFirewallRule -DisplayName "LANVC Server WebSocket (TCP 443)" -Direction Inbound -LocalPort 443 -Protocol TCP -Action Allow
```
