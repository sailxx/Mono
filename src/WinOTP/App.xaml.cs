using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinOTP.Services;

namespace WinOTP;

public partial class App : Application
{
    private Window? _window;
    private static readonly string LogPath = Path.Combine(DataPaths.Root, "startup.log");

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown AppDomain exception"));
        UnhandledException += (_, e) =>
        {
            Report(e.Exception);
            ShowError(e.Exception);
            // Do not continue after an unknown exception with potentially inconsistent vault state.
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
            ShowError(e);
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
            ShowError(e);
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

    // Exception messages may contain imported secrets or personal paths.
    private static void Report(Exception exception) =>
        Log($"STARTUP ERROR: {exception.GetType().Name} 0x{exception.HResult:X8}");

    private static void ShowError(Exception exception)
    {
        try { MessageBoxW(IntPtr.Zero, $"WinOTP could not continue safely. Your vault files have not been reset. Error: {exception.GetType().Name} (0x{exception.HResult:X8}).", "WinOTP: error", 0x10); }
        catch { }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
