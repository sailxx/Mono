using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace Mono;

public partial class App : Application
{
    private Window? _window;
    private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mono", "startup.log");

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject?.ToString() ?? "Unknown AppDomain exception");
        UnhandledException += (_, e) =>
        {
            Report(e.Exception.ToString());
            e.Handled = true;
            ShowError(e.Exception.ToString());
        };
        try
        {
            InitializeComponent();
            Log("App XAML initialized");
        }
        catch (Exception e)
        {
            Report(e.ToString());
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
            Report(e.ToString());
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

    private static void Report(string text) => Log($"STARTUP ERROR: {text}");

    private static void ShowError(string text)
    {
        try { MessageBoxW(IntPtr.Zero, text, "Mono — ошибка запуска", 0x10); }
        catch { }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
