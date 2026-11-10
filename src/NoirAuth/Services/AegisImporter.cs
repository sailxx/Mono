using System.Security.Cryptography;
using System.Text.Json;
using NoirAuth.Models;
using Org.BouncyCastle.Crypto.Generators;

namespace NoirAuth.Services;

public static class AegisImporter
{
    public static bool RequiresPassword(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("db", out var db) && db.ValueKind == JsonValueKind.String;
    }

    public static List<Account> Parse(string json, string? password = null)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement; var db = root.GetProperty("db"); byte[]? plain = null;
        try
        {
            if (db.ValueKind == JsonValueKind.String)
            {
                if (password is null) throw new FormatException("Для этой копии Aegis нужен пароль.");
                var header = root.GetProperty("header"); var encryptedDb = Convert.FromBase64String(db.GetString()!); var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password); byte[]? master = null;
                try
                {
                    foreach (var slot in header.GetProperty("slots").EnumerateArray())
                    {
                        if (!slot.TryGetProperty("type", out var slotType) || slotType.GetInt32() != 1) continue;
                        int n = slot.GetProperty("n").GetInt32(), r = slot.GetProperty("r").GetInt32(), p = slot.GetProperty("p").GetInt32();
                        if (n is < 16384 or > 32768 || (n & (n - 1)) != 0 || r is < 1 or > 8 || p is < 1 or > 4) throw new FormatException("Параметры scrypt в резервной копии выходят за поддерживаемые пределы.");
                        var derived = SCrypt.Generate(passwordBytes, Convert.FromHexString(slot.GetProperty("salt").GetString()!), n, r, p, 32);
                        var keyParams = slot.GetProperty("key_params"); var wrappedKey = Convert.FromHexString(slot.GetProperty("key").GetString()!); var unwrappedKey = new byte[wrappedKey.Length];
                        try { using var aes = new AesGcm(derived, 16); aes.Decrypt(Convert.FromHexString(keyParams.GetProperty("nonce").GetString()!), wrappedKey, Convert.FromHexString(keyParams.GetProperty("tag").GetString()!), unwrappedKey); master = unwrappedKey; break; }
                        catch (CryptographicException) { CryptographicOperations.ZeroMemory(unwrappedKey); }
                        finally { CryptographicOperations.ZeroMemory(derived); CryptographicOperations.ZeroMemory(wrappedKey); }
                    }
                    if (master is null) throw new FormatException("Неверный пароль Aegis или отсутствует парольный слот.");
                    var parameters = header.GetProperty("params"); plain = new byte[encryptedDb.Length];
                    using (var aes = new AesGcm(master, 16)) aes.Decrypt(Convert.FromHexString(parameters.GetProperty("nonce").GetString()!), encryptedDb, Convert.FromHexString(parameters.GetProperty("tag").GetString()!), plain);
                    CryptographicOperations.ZeroMemory(encryptedDb);
                }
                catch (CryptographicException) { throw new FormatException("Пароль неверен или файл Aegis повреждён."); }
                finally { if (master is not null) CryptographicOperations.ZeroMemory(master); CryptographicOperations.ZeroMemory(passwordBytes); CryptographicOperations.ZeroMemory(encryptedDb); }
                using var decrypted = JsonDocument.Parse(plain); db = decrypted.RootElement.Clone();
            }
            if (db.ValueKind != JsonValueKind.Object || !db.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) throw new FormatException("Некорректный формат базы Aegis.");
            var accounts = new List<Account>();
            foreach (var entry in entries.EnumerateArray())
            {
                var type = entry.GetProperty("type").GetString() ?? "totp"; if (type is not ("totp" or "hotp")) continue;
                var info = entry.GetProperty("info");
                accounts.Add(new Account { Issuer = OptionalString(entry, "issuer"), Name = OptionalString(entry, "name"), Secret = info.GetProperty("secret").GetString() ?? "", Algorithm = OptionalString(info, "algo", "SHA1").ToUpperInvariant(), Digits = OptionalInt(info, "digits", 6), Period = OptionalInt(info, "period", 30), Type = type.ToUpperInvariant(), Counter = OptionalLong(info, "counter", 0), Favorite = OptionalBool(entry, "favorite") });
            }
            return accounts;
        }
        catch (CryptographicException) { throw new FormatException("Пароль неверен или файл Aegis повреждён."); }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }

    private static string OptionalString(JsonElement element, string name, string fallback = "") => element.TryGetProperty(name, out var v) ? v.GetString() ?? fallback : fallback;
    private static int OptionalInt(JsonElement element, string name, int fallback) => element.TryGetProperty(name, out var v) ? v.GetInt32() : fallback;
    private static long OptionalLong(JsonElement element, string name, long fallback) => element.TryGetProperty(name, out var v) ? v.GetInt64() : fallback;
    private static bool OptionalBool(JsonElement element, string name) => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
