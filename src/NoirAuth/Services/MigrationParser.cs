using NoirAuth.Models;

namespace NoirAuth.Services;

public static class MigrationParser
{
    public static List<Account> ParseUri(string uri)
    {
        var u = new Uri(uri); if (!u.Scheme.Equals("otpauth-migration", StringComparison.OrdinalIgnoreCase)) throw new FormatException("Ожидается otpauth-migration URI.");
        var payload = System.Web.HttpUtility.ParseQueryString(u.Query)["data"] ?? throw new FormatException("В migration URI отсутствует параметр data.");
        using var stream = new MemoryStream(Convert.FromBase64String(payload.Replace(' ', '+'))); var result = new List<Account>();
        while (stream.Position < stream.Length)
        {
            var tag = ReadVarint(stream); var field = (int)(tag >> 3); var wire = (int)(tag & 7);
            if (field == 1 && wire == 2) { var length = ReadLength(stream); var bytes = new byte[length]; stream.ReadExactly(bytes); result.Add(ParseEntry(bytes)); }
            else SkipField(stream, wire);
        }
        if (result.Count == 0) throw new FormatException("В migration QR не найдено аккаунтов."); return result;
    }

    private static Account ParseEntry(byte[] bytes)
    {
        using var s = new MemoryStream(bytes); byte[] secret = []; string name = "", issuer = ""; int algorithm = 1, digits = 1, type = 2; long counter = 0;
        while (s.Position < s.Length)
        {
            var tag = ReadVarint(s); var field = (int)(tag >> 3); var wire = (int)(tag & 7);
            if (wire == 2)
            {
                var length = ReadLength(s); var data = new byte[length]; s.ReadExactly(data);
                if (field == 1) secret = data; else if (field == 2) name = System.Text.Encoding.UTF8.GetString(data); else if (field == 3) issuer = System.Text.Encoding.UTF8.GetString(data);
            }
            else if (wire == 0)
            {
                var value = ReadVarint(s); if (field == 4) algorithm = (int)value; else if (field == 5) digits = (int)value; else if (field == 6) type = (int)value; else if (field == 7) counter = (long)value;
            }
            else SkipField(s, wire);
        }
        if (secret.Length == 0) throw new FormatException("Migration-аккаунт не содержит секрет.");
        var label = name.Split(':', 2); return new Account { Issuer = issuer.Length > 0 ? issuer : label.Length > 1 ? label[0] : "Account", Name = label.Length > 1 ? label[1] : label[0], Secret = ToBase32(secret), Algorithm = algorithm switch { 2 => "SHA256", 3 => "SHA512", _ => "SHA1" }, Digits = digits == 2 ? 8 : 6, Type = type == 1 ? "HOTP" : "TOTP", Period = 30, Counter = counter };
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
        ulong value = 0; int shift = 0; while (shift < 70) { int b = s.ReadByte(); if (b < 0) throw new FormatException("Повреждён protobuf migration QR."); value |= (ulong)(b & 127) << shift; if ((b & 128) == 0) return value; shift += 7; }
        throw new FormatException("Некорректный protobuf varint.");
    }
    private static void SkipField(Stream s, int wire)
    {
        switch (wire) { case 0: _ = ReadVarint(s); break; case 1: s.Seek(8, SeekOrigin.Current); break; case 2: s.Seek(ReadLength(s), SeekOrigin.Current); break; case 5: s.Seek(4, SeekOrigin.Current); break; default: throw new FormatException("Неизвестный protobuf wire type."); }
        if (s.Position > s.Length) throw new FormatException("Обрезанный migration payload.");
    }
}
