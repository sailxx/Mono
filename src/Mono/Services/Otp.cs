using System.Security.Cryptography;
using Mono.Models;

namespace Mono.Services;

public static class Otp
{
    public static byte[] DecodeBase32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = text.Trim().Replace(" ", "").TrimEnd('=').ToUpperInvariant();
        if (clean.Length == 0) throw new FormatException("Введите секретный ключ.");
        var output = new List<byte>(); int buffer = 0, bits = 0;
        foreach (var ch in clean)
        {
            var v = alphabet.IndexOf(ch);
            if (v < 0) throw new FormatException("Secret Key должен быть в формате Base32.");
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { bits -= 8; output.Add((byte)(buffer >> bits)); }
        }
        return output.ToArray();
    }

    public static string Generate(Account account, long? unixSeconds = null, long hotpCounter = 0)
    {
        var secret = DecodeBase32(account.Secret);
        long counter = account.Type.Equals("HOTP", StringComparison.OrdinalIgnoreCase)
            ? (hotpCounter == 0 ? account.Counter : hotpCounter) : (unixSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()) / account.Period;
        Span<byte> message = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(message, counter);
        using HMAC hmac = account.Algorithm.ToUpperInvariant() switch
        {
            "SHA256" => new HMACSHA256(secret), "SHA512" => new HMACSHA512(secret), _ => new HMACSHA1(secret)
        };
        var hash = hmac.ComputeHash(message.ToArray()); int offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var mod = account.Digits == 8 ? 100_000_000 : 1_000_000;
        return (binary % mod).ToString(new string('0', account.Digits), System.Globalization.CultureInfo.InvariantCulture);
    }
}
