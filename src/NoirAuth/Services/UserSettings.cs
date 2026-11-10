using System.Security.Cryptography;
using System.Text.Json;

namespace NoirAuth.Services;

public sealed class UserSettings
{
    public bool HideCodes { get; set; } = true;
    public int ClipboardClearSeconds { get; set; } = 30;
    public int AutoLockMinutes { get; set; } = 5;
    public string? PasswordSalt { get; set; }
    public string? PasswordHash { get; set; }

    private static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoirAuth", "settings.bin");
    public static UserSettings Load()
    {
        if (!File.Exists(PathName)) return new();
        try { var bytes = ProtectedData.Unprotect(File.ReadAllBytes(PathName), null, DataProtectionScope.CurrentUser); try { return JsonSerializer.Deserialize<UserSettings>(bytes) ?? new(); } finally { CryptographicOperations.ZeroMemory(bytes); } }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!); var bytes = JsonSerializer.SerializeToUtf8Bytes(this);
        try { var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser); File.WriteAllBytes(PathName, protectedBytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void SetPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        PasswordSalt = Convert.ToBase64String(salt); PasswordHash = Convert.ToBase64String(hash); CryptographicOperations.ZeroMemory(hash); Save();
    }
    public bool VerifyPassword(string password)
    {
        if (PasswordSalt is null || PasswordHash is null) return true;
        try { var salt = Convert.FromBase64String(PasswordSalt); var expected = Convert.FromBase64String(PasswordHash); var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32); try { return CryptographicOperations.FixedTimeEquals(expected, actual); } finally { CryptographicOperations.ZeroMemory(actual); CryptographicOperations.ZeroMemory(expected); CryptographicOperations.ZeroMemory(salt); } }
        catch (FormatException) { return false; }
    }
    public bool HasPassword => PasswordSalt is not null && PasswordHash is not null;
}
