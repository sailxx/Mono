namespace WinOTP.Services;

public static class DataPaths
{
    private static readonly string LocalData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string Root { get; } = Path.Combine(LocalData, "WinOTP");
    private static bool _migrated;
    private static readonly object MigrationLock = new();

    public static string File(string name)
    {
        Migrate();
        return Path.Combine(Root, name);
    }

    // Copies (never moves) data from the legacy NoirAuth folder, so the old files stay as a fallback.
    private static void Migrate()
    {
        if (_migrated) return;
        lock (MigrationLock)
        {
            if (_migrated) return;
            var legacy = Path.Combine(LocalData, "NoirAuth");
            if (!Directory.Exists(legacy)) { _migrated = true; return; }
            Directory.CreateDirectory(Root);
            foreach (var name in new[] { "vault.bin", "settings.bin" })
            {
                var from = Path.Combine(legacy, name);
                var to = Path.Combine(Root, name);
                if (System.IO.File.Exists(from) && !System.IO.File.Exists(to)) System.IO.File.Copy(from, to, overwrite: false);
            }
            _migrated = true;
        }
    }
}
