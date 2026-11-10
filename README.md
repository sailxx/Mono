<div align="center">

**Русский** · [English](README.en.md)

<br>

<img src="assets/readme/hero.svg" width="100%" alt="Noir Authenticator — локальные коды двухфакторной аутентификации для Windows">

<a href="https://github.com/sailxx/NoirAuth/releases/latest/download/NoirAuth-win-x64.zip"><img src="assets/readme/download.svg" height="48" alt="Скачать для Windows 11"></a>

<sub><a href="#возможности">Возможности</a> · <a href="#защита-данных">Защита данных</a> · <a href="#установка">Установка</a> · <a href="#разработка">Разработка</a></sub>

</div>

<br>

<img src="assets/readme/features.svg" width="100%" alt="Одноразовые коды, DPAPI, импорт аккаунтов и минималистичный интерфейс">

**Noir Authenticator** — автономное приложение для Windows 11, которое хранит OTP-аккаунты на устройстве и генерирует коды TOTP/HOTP. Регистрация и облачная синхронизация не нужны.

## Возможности

- TOTP (RFC 6238) и HOTP (RFC 4226), SHA-1/SHA-256/SHA-512, 6 или 8 цифр.
- Добавление, редактирование, удаление, поиск, избранное, копирование кода.
- Импорт `otpauth://` и Google Authenticator migration URI, включая несколько записей в одном protobuf payload.
- Импорт QR-картинок из файла/буфера обмена, незашифрованных и зашифрованных Aegis JSON с предпросмотром выбора записей.
- Зашифрованное локальное хранилище и зашифрованная резервная копия.
- Необязательный мастер-пароль, автоблокировка и очистка скопированного кода из буфера.
- Чёрная OLED-тема и обновление оставшегося времени кода.

## Защита данных

Секреты сохраняются в файле локального профиля, зашифрованном Windows DPAPI для текущей учётной записи. Приложение не отправляет данные аккаунтов в сеть. Экспортируемая резервная копия защищена паролем AES-256-GCM + PBKDF2-SHA256 и переносима между профилями.

## Установка

1. Скачайте `NoirAuth-win-x64.zip` из [последнего релиза](https://github.com/sailxx/NoirAuth/releases/latest).
2. Распакуйте архив в постоянную папку и запустите `NoirAuth.exe`.

Архив содержит автономную сборку x64 и зависимости Windows App SDK. Рядом доступен SHA-256 файл для проверки целостности.

## Проверка алгоритмов

```powershell
dotnet run --project .\tests\OtpVectors\OtpVectors.csproj -c Release
```

Тестовый набор проверяет 10 HOTP и 18 TOTP официальных векторов RFC 4226/6238.

## Разработка

Нужны Windows 11, .NET 10 SDK и Visual Studio 2022 Build Tools с компонентами Windows App SDK.

```powershell
dotnet restore .\src\NoirAuth\NoirAuth.csproj
dotnet build .\src\NoirAuth\NoirAuth.csproj -c Release
```

GitHub Actions запускает RFC-векторы и сборку WinUI. Публикация тега `v*` создаёт portable ZIP с EXE, SHA-256 и GitHub build provenance.

<details>
<summary><b>Что ещё не включено</b></summary>

Сканирование QR веб-камерой, горячая клавиша, группы/категории, запуск с Windows и отдельная настройка темы/размера кодов пока не реализованы.

</details>

<br>

<div align="center"><sub><b>NOIR AUTH</b> · Локально. Спокойно. Без облака.</sub></div>
