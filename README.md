# LANVC — Virtual LAN Controller 🎮🌐

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-blue.svg)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Driver](https://img.shields.io/badge/Driver-Wintun%20(WHQL)-success.svg)](https://www.wintun.net/)
[![Transport](https://img.shields.io/badge/Transport-QUIC%20%2B%20WebSocket%20TLS%201.3-emerald.svg)](https://datatracker.ietf.org/doc/html/rfc9000)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**LANVC (LAN Virtual Controller)** — это современное приложение для создания защищённой виртуальной локальной сети между компьютерами через собственный выделенный сервер по топологии **Hub-and-Spoke**.

Разработано как производительная, открытая и безопасная замена **Radmin VPN** и **Hamachi** без сторонних облачных сервисов, платных подписок и привязки к чужим аккаунтам.

### Сравнение с аналогами:

| Возможность | LANVC | Radmin VPN | Hamachi |
| :--- | :---: | :---: | :---: |
| **Собственный сервер (Self-hosted)** | ✅ Да (Windows 11 Pro) | ❌ Сторонние сервера | ❌ Сторонние сервера |
| **Основной транспорт** | ⚡ **QUIC (UDP) + TLS 1.3** | Собственный TCP/UDP | Проприетарный P2P/Relay |
| **Резервный транспорт (обход DPI)** | 🚀 **WebSocket (HTTPS 443)** | ❌ Нет | ❌ Часто блокируется |
| **Драйвер сетевого адаптера** | 🛡️ **Wintun (WHQL, WireGuard)** | Проприетарный NDIS | Старый TAP-адаптер |
| **Обнаружение Minecraft LAN** | 🟢 **Встроенный Relay (порт 4445)** | ⚠️ Зависит от метрик | ⚠️ Часто не работает |
| **Шифрование паролей** | 🔒 **Argon2id + DPAPI** | N/A | MD5/SHA |
| **Отсутствие сторонних аккаунтов** | ✅ Подключение по IP и паролю | ❌ Нет гибкости | ❌ Обязательная регистрация |
