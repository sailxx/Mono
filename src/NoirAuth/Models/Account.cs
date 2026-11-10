namespace NoirAuth.Models;

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Issuer { get; set; } = "";
    public string Name { get; set; } = "";
    public string Secret { get; set; } = "";
    public string Algorithm { get; set; } = "SHA1";
    public int Digits { get; set; } = 6;
    public int Period { get; set; } = 30;
    public long Counter { get; set; }
    public string Type { get; set; } = "TOTP";
    public bool Favorite { get; set; }
}
