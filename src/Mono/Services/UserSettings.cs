using System.Security.Cryptography;
using System.Text.Json;

namespace Mono.Services;

public sealed class UserSettings
{
    public bool HideCodes { get; set; } = true;
    public int ClipboardClearSeconds { get; set; } = 30;
    public int AutoLockMinutes { get; set; } = 5;
    public string? PasswordSalt { get; set; }
    public string? PasswordHash { get; set; }

    private string _path;
    public UserSettings() : this(null) { }
    public UserSettings(string? path) => _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoirAuth", "settings.bin");
    public static UserSettings Load(string? path = null)
    {
        if (path is null) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoirAuth", "settings.bin");
        if (!File.Exists(path)) return new(path);
        byte[]? protectedBytes = null;
        try
        {
            protectedBytes = File.ReadAllBytes(path);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            try
            {
                var settings = JsonSerializer.Deserialize<UserSettings>(bytes) ?? new();
                settings._path = path;
                return settings;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (CryptographicException) { throw new InvalidOperationException("Не удалось расшифровать настройки Windows DPAPI. Файл сохранён, чтобы не отключать защиту незаметно."); }
        catch (JsonException) { throw new InvalidOperationException("Файл настроек повреждён. Он сохранён без изменений."); }
        finally { if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!); var bytes = JsonSerializer.SerializeToUtf8Bytes(this);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var temp = _path + ".tmp";
            try { File.WriteAllBytes(temp, protectedBytes); if (File.Exists(_path)) File.Replace(temp, _path, null); else File.Move(temp, _path); }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void SetPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        try { PasswordSalt = Convert.ToBase64String(salt); PasswordHash = Convert.ToBase64String(hash); Save(); }
        finally { CryptographicOperations.ZeroMemory(hash); CryptographicOperations.ZeroMemory(salt); }
    }
    public bool VerifyPassword(string password)
    {
        if (PasswordSalt is null || PasswordHash is null) return true;
        byte[]? salt = null, expected = null, actual = null;
        try
        {
            salt = Convert.FromBase64String(PasswordSalt);
            expected = Convert.FromBase64String(PasswordHash);
            actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
        finally
        {
            if (actual is not null) CryptographicOperations.ZeroMemory(actual);
            if (expected is not null) CryptographicOperations.ZeroMemory(expected);
            if (salt is not null) CryptographicOperations.ZeroMemory(salt);
        }
    }
    public bool HasPassword => PasswordSalt is not null && PasswordHash is not null;
}
