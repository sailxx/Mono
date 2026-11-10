<div align="center">

[Русский](README.md) · **English**

<br>

<img src="assets/readme/hero.svg" width="100%" alt="Noir Authenticator — local two-factor codes for Windows">

<a href="https://github.com/sailxx/NoirAuth/releases/latest/download/NoirAuth-win-x64.zip"><img src="assets/readme/download.svg" height="48" alt="Download for Windows 11"></a>

<sub><a href="#features">Features</a> · <a href="#data-protection">Data protection</a> · <a href="#install">Install</a> · <a href="#development">Development</a></sub>

</div>

<br>

<img src="assets/readme/features.svg" width="100%" alt="One-time codes, DPAPI, account import and a minimalist interface">

**Noir Authenticator** is an offline Windows 11 app that keeps OTP accounts on your device and generates TOTP/HOTP codes. No sign-up or cloud sync.

## Features

- TOTP (RFC 6238) and HOTP (RFC 4226), SHA-1/SHA-256/SHA-512, 6 or 8 digits.
- Add, edit, delete, search, favorite and copy codes.
- Import `otpauth://` and Google Authenticator migration URIs, including multiple accounts in a protobuf payload.
- Import QR images from a file or clipboard, and encrypted or unencrypted Aegis JSON with a selectable preview.
- Encrypted local vault and encrypted backup.
- Optional master password, automatic lock and clipboard clearing.
- OLED black theme with a live code timer.

## Data protection

Secrets live in the local profile, protected by Windows DPAPI for the current Windows user. The app does not send account data over the network. Exported backups use a portable password-based AES-256-GCM + PBKDF2-SHA256 envelope.

## Install

1. Download `NoirAuth-win-x64.zip` from the [latest release](https://github.com/sailxx/NoirAuth/releases/latest).
2. Extract it to a permanent folder and run `NoirAuth.exe`.

The archive contains a self-contained x64 build and Windows App SDK dependencies. A SHA-256 file is provided to verify integrity.

## Development

Requires Windows 11, .NET 10 SDK and Visual Studio 2022 Build Tools with Windows App SDK components.

```powershell
dotnet run --project .\tests\OtpVectors\OtpVectors.csproj -c Release
dotnet build .\src\NoirAuth\NoirAuth.csproj -c Release
```

The test console checks 10 HOTP and 18 TOTP RFC vectors plus Google migration protobuf and encrypted/plain Aegis JSON samples. GitHub Actions also builds WinUI; pushing a `v*` tag publishes a portable ZIP, SHA-256 and GitHub build provenance.

<details>
<summary><b>Not included yet</b></summary>

Camera-based QR scanning, global hotkeys, account groups/categories, Windows startup and theme/code-size preferences are not implemented yet.

</details>

<br><div align="center"><sub><b>NOIR AUTH</b> · Local. Quiet. No cloud.</sub></div>
