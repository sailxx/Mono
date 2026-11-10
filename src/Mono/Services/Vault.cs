using System.Security.Cryptography;
using System.Text.Json;
using Mono.Models;

namespace Mono.Services;

public sealed class Vault
{
    private readonly string _path;
    public Vault(string? path = null) => _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoirAuth", "vault.bin");
    public List<Account> Load()
    {
        if (!File.Exists(_path)) return [];
        var protectedBytes = File.ReadAllBytes(_path);
        try
        {
            var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            try
            {
                var accounts = JsonSerializer.Deserialize<List<Account>>(plain) ?? [];
                var accountIds = new HashSet<Guid>();
                foreach (var account in accounts)
                {
                    if (account is null) throw new InvalidOperationException("Хранилище содержит пустую запись аккаунта.");
                    account.Issuer ??= "";
                    account.Name ??= "";
                    Otp.Validate(account);
                    if (account.Id == Guid.Empty || !accountIds.Add(account.Id))
                    {
                        account.Id = Guid.NewGuid();
                        accountIds.Add(account.Id);
                    }
                }
                return accounts;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (CryptographicException) { throw new InvalidOperationException("Не удалось расшифровать хранилище текущей учётной записью Windows."); }
        catch (JsonException) { throw new InvalidOperationException("Файл хранилища повреждён."); }
        finally { CryptographicOperations.ZeroMemory(protectedBytes); }
    }
    public void Save(List<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (accounts.Any(account => account is null)) throw new InvalidOperationException("Нельзя сохранить пустую запись аккаунта.");
        foreach (var account in accounts) Otp.Validate(account);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(accounts);
        try
        {
            var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            var temp = _path + ".tmp";
            try { File.WriteAllBytes(temp, protectedBytes); if (File.Exists(_path)) File.Replace(temp, _path, null); else File.Move(temp, _path); }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
