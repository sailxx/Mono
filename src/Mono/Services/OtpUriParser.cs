using System.Globalization;
using Mono.Models;

namespace Mono.Services;

public static class OtpUriParser
{
    public static Account Parse(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Ожидается URI otpauth://.");

        var isHotp = uri.Host.Equals("hotp", StringComparison.OrdinalIgnoreCase);
        if (!isHotp && !uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Тип URI должен быть totp или hotp.");

        var label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var parts = label.Split(':', 2);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var account = new Account
        {
            Type = isHotp ? "HOTP" : "TOTP",
            Issuer = NonEmpty(query["issuer"]) ?? (parts.Length > 1 ? parts[0] : "Account"),
            Name = parts.Length > 1 ? parts[1] : parts[0],
            Secret = query["secret"] ?? throw new FormatException("В URI отсутствует secret."),
            Algorithm = (query["algorithm"] ?? "SHA1").ToUpperInvariant(),
            Digits = ParseOptionalInt(query["digits"], 6, "digits"),
            Period = ParseOptionalInt(query["period"], 30, "period"),
            Counter = isHotp ? ParseRequiredLong(query["counter"], "counter") : 0
        };
        Otp.Validate(account);
        return account;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int ParseOptionalInt(string? value, int fallback, string parameter)
    {
        if (value is null) return fallback;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            throw new FormatException($"Параметр {parameter} в otpauth URI задан неверно.");
        return parsed;
    }

    private static long ParseRequiredLong(string? value, string parameter)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            throw new FormatException($"Для HOTP в URI требуется неотрицательный параметр {parameter}.");
        return parsed;
    }
}
