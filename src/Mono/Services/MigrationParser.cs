using Mono.Models;
using System.Security.Cryptography;

namespace Mono.Services;

public static class MigrationParser
{
    public static List<Account> ParseUri(string uri)
    {
        var u = new Uri(uri); if (!u.Scheme.Equals("otpauth-migration", StringComparison.OrdinalIgnoreCase)) throw new FormatException("Ожидается otpauth-migration URI.");
        var payload = System.Web.HttpUtility.ParseQueryString(u.Query)["data"] ?? throw new FormatException("В migration URI отсутствует параметр data.");
        var payloadBytes = Convert.FromBase64String(payload.Replace(' ', '+'));
        var result = new List<Account>();
        try
        {
            using var stream = new MemoryStream(payloadBytes, writable: false);
            while (stream.Position < stream.Length)
            {
                var (field, wire) = ReadTag(stream);
                if (field == 1 && wire == 2)
                {
                    var length = ReadLength(stream); var bytes = new byte[length]; stream.ReadExactly(bytes);
                    try { result.Add(ParseEntry(bytes)); }
                    finally { CryptographicOperations.ZeroMemory(bytes); }
                }
                else SkipField(stream, wire);
            }
            if (result.Count == 0) throw new FormatException("В migration QR не найдено аккаунтов.");
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(payloadBytes); }
    }

    private static Account ParseEntry(byte[] bytes)
    {
        using var s = new MemoryStream(bytes, writable: false); byte[] secret = []; string name = "", issuer = ""; int algorithm = 1, digits = 1, type = 2; long counter = 0;
        try
        {
            while (s.Position < s.Length)
            {
                var (field, wire) = ReadTag(s);
                if (wire == 2)
                {
                    var length = ReadLength(s); var data = new byte[length]; s.ReadExactly(data);
                    if (field == 1) { CryptographicOperations.ZeroMemory(secret); secret = data; }
                    else if (field == 2) name = System.Text.Encoding.UTF8.GetString(data);
                    else if (field == 3) issuer = System.Text.Encoding.UTF8.GetString(data);
                }
                else if (wire == 0)
                {
                    var value = ReadVarint(s); if (field == 4) algorithm = value <= int.MaxValue ? (int)value : -1; else if (field == 5) digits = value <= int.MaxValue ? (int)value : -1; else if (field == 6) type = value <= int.MaxValue ? (int)value : -1; else if (field == 7) counter = value <= long.MaxValue ? (long)value : -1;
                }
                else SkipField(s, wire);
            }
            if (secret.Length == 0) throw new FormatException("Migration-аккаунт не содержит секрет.");
            var label = name.Split(':', 2);
            var account = new Account { Issuer = issuer.Length > 0 ? issuer : label.Length > 1 ? label[0] : "Account", Name = label.Length > 1 ? label[1] : label[0], Secret = ToBase32(secret), Algorithm = algorithm switch { 0 or 1 => "SHA1", 2 => "SHA256", 3 => "SHA512", _ => "UNSUPPORTED" }, Digits = digits switch { 0 or 1 => 6, 2 => 8, _ => 0 }, Type = type switch { 1 => "HOTP", 2 => "TOTP", _ => "UNSUPPORTED" }, Period = 30, Counter = counter };
            Otp.Validate(account);
            return account;
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
    private static string ToBase32(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; var output = new System.Text.StringBuilder(); int buffer = 0, bits = 0;
        foreach (var b in bytes) { buffer = (buffer << 8) | b; bits += 8; while (bits >= 5) { bits -= 5; output.Append(alphabet[(buffer >> bits) & 31]); } }
        if (bits > 0) output.Append(alphabet[(buffer << (5 - bits)) & 31]); return output.ToString();
    }
    private static int ReadLength(Stream stream)
    {
        var length = ReadVarint(stream); if (length > int.MaxValue || stream.Position + (long)length > stream.Length) throw new FormatException("Обрезанное поле migration protobuf."); return (int)length;
    }
    private static ulong ReadVarint(Stream s)
    {
        ulong value = 0;
        for (var index = 0; index < 10; index++)
        {
            var b = s.ReadByte();
            if (b < 0) throw new FormatException("Повреждён protobuf migration QR.");
            if (index == 9 && b > 1) throw new FormatException("Переполнение protobuf varint.");
            value |= (ulong)(b & 127) << (index * 7);
            if ((b & 128) == 0) return value;
        }
        throw new FormatException("Некорректный protobuf varint.");
    }
    private static (int Field, int Wire) ReadTag(Stream stream)
    {
        var tag = ReadVarint(stream);
        var field = tag >> 3;
        var wire = (int)(tag & 7);
        if (field is 0 or > 536870911 || wire is 3 or 4 or > 5)
            throw new FormatException("Некорректный protobuf migration QR: номер поля или тип данных недопустим.");
        return ((int)field, wire);
    }
    private static void SkipField(Stream s, int wire)
    {
        switch (wire) { case 0: _ = ReadVarint(s); break; case 1: s.Seek(8, SeekOrigin.Current); break; case 2: s.Seek(ReadLength(s), SeekOrigin.Current); break; case 5: s.Seek(4, SeekOrigin.Current); break; default: throw new FormatException("Неизвестный protobuf wire type."); }
        if (s.Position > s.Length) throw new FormatException("Обрезанный migration payload.");
    }
}
