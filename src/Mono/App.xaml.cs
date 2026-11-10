using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace Mono;

public partial class App : Application
{
    private Window? _window;
    private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mono", "startup.log");

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown AppDomain exception"));
        UnhandledException += (_, e) =>
        {
            Report(e.Exception);
            e.Handled = true;
            ShowError(e.Exception.ToString());
        };
        try
        {
            InitializeComponent();
            DebugSettings.XamlResourceReferenceFailed += (_, e) => Log($"XAML resource reference failed: {e.Message}");
            Log("App XAML initialized");
        }
        catch (Exception e)
        {
            Report(e);
            ShowError(e.ToString());
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Log("OnLaunched entered");
            _window = new MainWindow();
            Log("MainWindow constructed");
            _window.Activate();
            Log("MainWindow activated");
        }
        catch (Exception e)
        {
            Report(e);
            ShowError(e.ToString());
            throw;
        }
    }

    private static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}");
        }
        catch { }
    }

    private static void Report(Exception exception)
    {
        var details = new System.Text.StringBuilder(exception.ToString());
        details.AppendLine($"HResult: 0x{exception.HResult:X8}");
        if (exception.InnerException is not null) details.AppendLine($"Inner exception: {exception.InnerException}");
        foreach (var property in exception.GetType().GetProperties())
        {
            try { details.AppendLine($"{property.Name}: {property.GetValue(exception)}"); }
            catch { }
        }
        Log($"STARTUP ERROR: {details}");
    }

    private static void ShowError(string text)
    {
        try { MessageBoxW(IntPtr.Zero, text, "Mono — ошибка запуска", 0x10); }
        catch { }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
