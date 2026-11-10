using System.Security.Cryptography;
using System.Text.Json;
using Mono.Models;

namespace Mono.Services;

public sealed class Vault
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoirAuth", "vault.bin");
    public List<Account> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<List<Account>>(plain) ?? []; }
            finally { CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(protectedBytes); }
        }
        catch (CryptographicException) { throw new InvalidOperationException("Не удалось расшифровать хранилище текущей учётной записью Windows."); }
        catch (JsonException) { throw new InvalidOperationException("Файл хранилища повреждён."); }
    }
    public void Save(List<Account> accounts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(accounts);
        try
        {
            var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            try { var temp = _path + ".tmp"; File.WriteAllBytes(temp, protectedBytes); if (File.Exists(_path)) File.Replace(temp, _path, null); else File.Move(temp, _path); }
            finally { CryptographicOperations.ZeroMemory(protectedBytes); }
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
