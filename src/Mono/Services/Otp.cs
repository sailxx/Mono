using System.Security.Cryptography;
using Mono.Models;

namespace Mono.Services;

public static class Otp
{
    public static byte[] DecodeBase32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Введите секретный ключ.");
        var clean = string.Concat(text.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        var paddingAt = clean.IndexOf('=');
        var symbolCount = paddingAt < 0 ? clean.Length : paddingAt;
        if (paddingAt >= 0 && clean.AsSpan(paddingAt).IndexOfAnyExcept('=') >= 0)
            throw new FormatException("Некорректное заполнение Base32.");
        var remainder = symbolCount % 8;
        var expectedPadding = remainder switch { 0 => 0, 2 => 6, 4 => 4, 5 => 3, 7 => 1, _ => -1 };
        if (expectedPadding < 0 || (paddingAt >= 0 && clean.Length - symbolCount != expectedPadding))
            throw new FormatException("Некорректная длина ключа Base32.");
        clean = clean[..symbolCount];
        if (clean.Length == 0) throw new FormatException("Введите секретный ключ.");
        var output = new List<byte>(); int buffer = 0, bits = 0;
        foreach (var ch in clean)
        {
            var v = alphabet.IndexOf(ch);
            if (v < 0) throw new FormatException("Secret Key должен быть в формате Base32.");
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { bits -= 8; output.Add((byte)(buffer >> bits)); }
        }
        if (bits > 0 && (buffer & ((1 << bits) - 1)) != 0)
            throw new FormatException("Ключ Base32 содержит некорректные завершающие биты.");
        if (output.Count == 0) throw new FormatException("Секретный ключ слишком короткий.");
        return output.ToArray();
    }

    public static string Generate(Account account, long? unixSeconds = null, long? hotpCounter = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        ValidateParameters(account);
        var secret = DecodeBase32(account.Secret);
        Span<byte> hash = stackalloc byte[64];
        try
        {
            long counter;
            if (account.Type.Equals("HOTP", StringComparison.OrdinalIgnoreCase))
                counter = hotpCounter ?? account.Counter;
            else
            {
                var seconds = unixSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(unixSeconds), "Время TOTP не может быть раньше начала эпохи Unix.");
                counter = seconds / account.Period;
            }
            if (counter < 0) throw new ArgumentOutOfRangeException(nameof(hotpCounter), "Счётчик HOTP не может быть отрицательным.");
            Span<byte> message = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(message, counter);
            using HMAC hmac = account.Algorithm.ToUpperInvariant() switch
            {
                "SHA1" => new HMACSHA1(secret), "SHA256" => new HMACSHA256(secret), "SHA512" => new HMACSHA512(secret),
                _ => throw new FormatException("Поддерживаются SHA1, SHA256 и SHA512.")
            };
            if (!hmac.TryComputeHash(message, hash, out var hashLength)) throw new CryptographicException("Не удалось вычислить OTP-код.");
            var offset = hash[hashLength - 1] & 0x0f;
            var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
            var mod = account.Digits == 8 ? 100_000_000 : 1_000_000;
            return (binary % mod).ToString(new string('0', account.Digits), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    public static void Validate(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        ValidateParameters(account);
        var secret = DecodeBase32(account.Secret);
        CryptographicOperations.ZeroMemory(secret);
    }

    private static void ValidateParameters(Account account)
    {
        if (string.IsNullOrWhiteSpace(account.Secret)) throw new FormatException("Введите секретный ключ.");
        if (account.Digits is not (6 or 8)) throw new FormatException("OTP-код должен содержать 6 или 8 цифр.");
        if (!string.Equals(account.Algorithm, "SHA1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(account.Algorithm, "SHA256", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(account.Algorithm, "SHA512", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Поддерживаются SHA1, SHA256 и SHA512.");
        if (!string.Equals(account.Type, "TOTP", StringComparison.OrdinalIgnoreCase) && !string.Equals(account.Type, "HOTP", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Тип кода должен быть TOTP или HOTP.");
        if (string.Equals(account.Type, "TOTP", StringComparison.OrdinalIgnoreCase) && account.Period is < 1 or > 86400)
            throw new FormatException("Период TOTP должен быть от 1 секунды до 24 часов.");
        if (account.Counter < 0) throw new FormatException("Счётчик HOTP не может быть отрицательным.");
    }
}
