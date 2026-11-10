using NoirAuth.Models;
using NoirAuth.Services;

var hotpKey = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
string[] hotp = ["755224", "287082", "359152", "969429", "338314", "254676", "287922", "162583", "399871", "520489"];
for (var i = 0; i < hotp.Length; i++) Check("HOTP", hotp[i], Otp.Generate(new Account { Secret = hotpKey, Type = "HOTP", Counter = i }));

var vectors = new (string Secret, string Algorithm, long Time, string Expected)[]
{
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "SHA1", 59, "94287082"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "SHA1", 1111111109, "07081804"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "SHA1", 1111111111, "14050471"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "SHA1", 1234567890, "89005924"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "SHA1", 2000000000, "69279037"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "SHA1", 20000000000, "65353130"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA", "SHA256", 59, "46119246"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA", "SHA256", 1111111109, "68084774"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA", "SHA256", 1111111111, "67062674"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA", "SHA256", 1234567890, "91819424"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA", "SHA256", 2000000000, "90698825"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA", "SHA256", 20000000000, "77737706"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNA", "SHA512", 59, "90693936"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNA", "SHA512", 1111111109, "25091201"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNA", "SHA512", 1111111111, "99943326"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNA", "SHA512", 1234567890, "93441116"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNA", "SHA512", 2000000000, "38618901"),
    ("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNA", "SHA512", 20000000000, "47863826"),
};
foreach (var v in vectors) Check($"TOTP {v.Algorithm} at {v.Time}", v.Expected, Otp.Generate(new Account { Secret = v.Secret, Algorithm = v.Algorithm, Digits = 8, Period = 30 }, v.Time));
using var entry = new MemoryStream();
WriteBytes(entry, 1, System.Text.Encoding.ASCII.GetBytes("12345678901234567890"));
WriteBytes(entry, 2, System.Text.Encoding.UTF8.GetBytes("GitHub:sample@example.test"));
WriteBytes(entry, 3, System.Text.Encoding.UTF8.GetBytes("GitHub"));
WriteVarint(entry, 4, 1); WriteVarint(entry, 5, 1); WriteVarint(entry, 6, 1); WriteVarint(entry, 7, 0);
using var payload = new MemoryStream(); WriteBytes(payload, 1, entry.ToArray());
var migration = NoirAuth.Services.MigrationParser.ParseUri("otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String(payload.ToArray())));
if (migration.Count != 1 || migration[0].Issuer != "GitHub" || migration[0].Name != "sample@example.test" || migration[0].Type != "HOTP" || Otp.Generate(migration[0]) != "755224") throw new Exception("Google Authenticator migration protobuf import vector failed.");
Console.WriteLine($"Passed {hotp.Length} HOTP, {vectors.Length} TOTP RFC vectors, and Google Authenticator migration protobuf import.");

static void Check(string name, string expected, string actual)
{
    if (expected != actual) throw new Exception($"{name}: expected {expected}, got {actual}");
}

static void WriteBytes(Stream stream, int field, byte[] data) { WriteRawVarint(stream, (ulong)((field << 3) | 2)); WriteRawVarint(stream, (ulong)data.Length); stream.Write(data); }
static void WriteVarint(Stream stream, int field, ulong value) { WriteRawVarint(stream, (ulong)(field << 3)); WriteRawVarint(stream, value); }
static void WriteRawVarint(Stream stream, ulong value) { while (value >= 128) { stream.WriteByte((byte)(value | 128)); value >>= 7; } stream.WriteByte((byte)value); }
