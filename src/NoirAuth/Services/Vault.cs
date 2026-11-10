using System.Security.Cryptography;
using System.Text.Json;
using NoirAuth.Models;

namespace NoirAuth.Services;

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
            return JsonSerializer.Deserialize<List<Account>>(plain) ?? [];
        }
        catch (CryptographicException) { throw new InvalidOperationException("Не удалось расшифровать хранилище текущей учётной записью Windows."); }
        catch (JsonException) { throw new InvalidOperationException("Файл хранилища повреждён."); }
    }
    public void Save(List<Account> accounts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(accounts);
        var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        if (File.Exists(_path)) File.Replace(temp, _path, null); else File.Move(temp, _path);
        CryptographicOperations.ZeroMemory(plain);
    }
}
