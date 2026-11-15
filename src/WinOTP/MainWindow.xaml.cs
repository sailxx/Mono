using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT.Interop;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using ZXing;
using ZXing.Common;
using FormatException = System.FormatException;
using WinOTP.Models;
using WinOTP.Services;

namespace WinOTP;

public sealed partial class MainWindow : Window
{
    private readonly Vault _vault = new();
    private UserSettings _settings = UserSettings.Load();
    private List<Account> _accounts = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Dictionary<Guid, (TextBlock Code, TextBlock Time)> _cardLabels = [];
    private readonly Dictionary<Guid, long> _codeSteps = [];
    private DateTime _lastActivity = DateTime.Now;
    private bool _locked;
    private bool _loadFailed;
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = null;
        var windowHandle = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle));
        var titleBar = appWindow.TitleBar;
        titleBar.IconShowOptions = IconShowOptions.ShowIconAndSystemMenu;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "WinOTP.ico");
        if (File.Exists(iconPath)) appWindow.SetIcon(iconPath);
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            titleBar.BackgroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 0, 0, 0);
            titleBar.ForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 235, 235, 235);
            titleBar.InactiveBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 0, 0, 0);
            titleBar.InactiveForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 170, 170, 170);
            titleBar.ButtonBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 0, 0, 0);
            titleBar.ButtonForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 235, 235, 235);
            titleBar.ButtonHoverBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 38);
            titleBar.ButtonHoverForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 65, 65, 65);
            titleBar.ButtonPressedForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255);
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 0, 0, 0);
            titleBar.ButtonInactiveForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 170, 170, 170);
        }
        appWindow.Resize(new SizeInt32(660, 700));
        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = version is null ? "v0.3.1" : $"v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        ShowCodesButton.Content = _settings.HideCodes ? "Show codes" : "Hide codes";
        if (Content is UIElement root) { root.PointerPressed += (_, _) => Touch(); root.KeyDown += (_, _) => Touch(); }
        _timer.Tick += async (_, _) =>
        {
            try
            {
                ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
                if (!_locked && _settings.HasPassword && _settings.AutoLockMinutes > 0 && DateTime.Now - _lastActivity >= TimeSpan.FromMinutes(_settings.AutoLockMinutes)) await LockAndUnlock();
                else UpdateCodes();
            }
            catch (Exception ex) { LogStartup($"Timer action failed: {ex.GetType().Name}"); }
        };
        _timer.Start();
        ((FrameworkElement)Content).Loaded += async (_, _) =>
        {
            try
            {
                if (_settings.HasPassword) await Unlock();
                else _accounts = _vault.Load();
                RenderAccounts();
                LogStartup("Initial account list rendered");
            }
            catch (Exception ex)
            {
                _loadFailed = true;
                LogStartup($"Initial screen failed: {ex.GetType().Name}");
                if (Content is Control control) control.IsEnabled = false;
                else if (Content is Panel panel) foreach (var child in panel.Children.OfType<Control>()) child.IsEnabled = false;
                _timer.Stop();
                // Disable every action after a failed load, rather than risk saving an empty vault.
                if (Content is UIElement failedRoot) failedRoot.IsHitTestVisible = false;
                AccountList.Children.Clear();
                AccountList.Children.Add(new TextBlock
                {
                    Text = "Unable to load your vault. Your existing files have not been replaced.",
                    Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255)),
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        };
    }

    private static void LogStartup(string message)
    {
        try
        {
            var directory = DataPaths.Root;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }

    private void RenderAccounts()
    {
        var q = SearchBox.Text.Trim();
        var visible = _accounts.Where(a => q.Length == 0 || a.Issuer.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Favorite).ThenBy(a => a.Issuer).ThenBy(a => a.Name).ToList();
        AccountList.Children.Clear(); _cardLabels.Clear(); _codeSteps.Clear();
        CountText.Text = $"{_accounts.Count} ACCOUNTS · LOCAL";
        foreach (var account in visible) AccountList.Children.Add(BuildCard(account));
        if (visible.Count == 0)
        {
            var empty = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 100, 0, 0) };
            empty.Children.Add(new TextBlock { Text = _accounts.Count == 0 ? "Your codes belong here" : "No matching accounts", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            empty.Children.Add(new TextBlock { Text = _accounts.Count == 0 ? "Add an account or import a backup" : "Try a different name or service", Foreground = Brush("#A0A0A0"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
            AccountList.Children.Add(empty);
        }
    }

    private Border BuildCard(Account a)
    {
        string code = "••••••"; string error = "";
        try { code = Otp.Generate(a); } catch (Exception) { error = "Invalid account parameters"; }
        var isHotp = string.Equals(a.Type, "HOTP", StringComparison.OrdinalIgnoreCase);
        var remaining = isHotp ? "Updated on copy" : a.Period > 0 ? $"Refreshes in {a.Period - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % a.Period}s" : "Invalid TOTP period";
        var title = new TextBlock { Text = a.Issuer, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("#EEEEEE") };
        var name = new TextBlock { Text = a.Name, FontSize = 12, Foreground = Brush("#777777") };
        var codeBlock = new TextBlock { Text = _settings.HideCodes ? new string('•', a.Digits) : code, FontFamily = new FontFamily("ms-appx:///Assets/Fonts/CascadiaCode.ttf#Cascadia Code"), FontSize = 27, CharacterSpacing = 0, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = error.Length > 0 ? Brush("#B0B0B0") : Brush("#F1F1F1"), VerticalAlignment = VerticalAlignment.Center };
        var metadata = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        metadata.Children.Add(title); metadata.Children.Add(name);
        var codeStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var timeBlock = new TextBlock { Text = error.Length > 0 ? error : remaining, FontSize = 10, Foreground = Brush("#929292") };
        codeStack.Children.Add(codeBlock); codeStack.Children.Add(timeBlock);
        _cardLabels[a.Id] = (codeBlock, timeBlock);
        _codeSteps[a.Id] = isHotp ? a.Counter : a.Period > 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() / a.Period : long.MinValue;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var copy = new Button { Content = "Copy code", Background = Brush("#171717"), Foreground = Brush("#E0E0E0"), CornerRadius = new CornerRadius(10), Padding = new Thickness(11, 8, 11, 8), IsEnabled = error.Length == 0, FontFamily = new FontFamily("ms-appx:///Assets/Fonts/CascadiaCode.ttf#Cascadia Code") };
        copy.Click += async (_, _) =>
        {
            Touch();
            try
            {
                var current = Otp.Generate(a);
                if (isHotp)
                {
                    if (a.Counter == long.MaxValue) throw new InvalidOperationException("HOTP counter has reached its maximum value.");
                    a.Counter++;
                    try { Save(); } catch { a.Counter--; throw; }
                    RenderAccounts();
                }
                var package = new DataPackage(); package.SetText(current);
                if (!Clipboard.SetContentWithOptions(package, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false }))
                    throw new InvalidOperationException("Clipboard is busy. Try again.");
                if (_settings.ClipboardClearSeconds > 0) _ = ClearClipboardAfter(current, _settings.ClipboardClearSeconds);
                copy.Content = "Copied"; await Task.Delay(1100); copy.Content = "Copy code";
            }
            catch (Exception) { await ShowError("Code not copied", "Check the account and vault access, then try again. A HOTP counter may have advanced if the clipboard was busy."); }
        };
        var pin = new Button { Content = new FontIcon { Glyph = a.Favorite ? "\uE735" : "\uE734", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14 }, Background = Brush("#171717"), Foreground = Brush("#FFFFFF"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8) };
        AutomationProperties.SetName(pin, a.Favorite ? "Remove from favorites" : "Add to favorites");
        pin.Click += (_, _) => { a.Favorite = !a.Favorite; Save(); RenderAccounts(); };
        var edit = new Button { Content = new FontIcon { Glyph = "\uE712", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14 }, Background = Brush("#171717"), Foreground = Brush("#BDBDBD"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8) };
        AutomationProperties.SetName(edit, "Account actions");
        edit.Click += (_, _) => { var menu = new MenuFlyout(); var editItem = new MenuFlyoutItem { Text = "Edit" }; editItem.Click += async (_, _) => await EditAccount(a); var deleteItem = new MenuFlyoutItem { Text = "Delete" }; deleteItem.Click += async (_, _) => { var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Delete account?", Content = $"{a.Issuer} · {a.Name}", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }; if (await dlg.ShowAsync() == ContentDialogResult.Primary) { _accounts.Remove(a); Save(); RenderAccounts(); } }; menu.Items.Add(editItem); menu.Items.Add(deleteItem); menu.ShowAt(edit); };
        actions.Children.Add(pin); actions.Children.Add(edit);
        var top = new Grid { ColumnSpacing = 8 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(metadata); Grid.SetColumn(actions, 1); top.Children.Add(actions);
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(16, 14, 16, 14) };
        body.Children.Add(top);
        var codeRow = new Grid { ColumnSpacing = 16 };
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        codeRow.Children.Add(codeStack);
        Grid.SetColumn(copy, 1); codeRow.Children.Add(copy);
        body.Children.Add(codeRow);
        return new Border { Background = Brush("#101010"), BorderBrush = Brush("#242424"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Child = body };
    }

    private async Task EditAccount(Account? edit = null)
    {
        if (_loadFailed || _locked) return;
        var issuer = Input("Service", edit?.Issuer ?? ""); var name = Input("Account / email", edit?.Name ?? "");
        var secret = PasswordInput("Secret key (Base32)", 300); secret.Password = edit?.Secret ?? "";
        var algo = new ComboBox { ItemsSource = new[] { "SHA1", "SHA256", "SHA512" }, SelectedItem = edit?.Algorithm ?? "SHA1", HorizontalAlignment = HorizontalAlignment.Stretch };
        var digits = new ComboBox { ItemsSource = new[] { "6", "8" }, SelectedItem = (edit?.Digits ?? 6).ToString(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var period = Input("Period in seconds", (edit?.Period ?? 30).ToString());
        var type = new ComboBox { ItemsSource = new[] { "TOTP", "HOTP" }, SelectedItem = edit?.Type ?? "TOTP", HorizontalAlignment = HorizontalAlignment.Stretch };
        var form = new StackPanel { Spacing = 11, Width = 380 }; form.Children.Add(issuer); form.Children.Add(name); form.Children.Add(secret);
        form.Children.Add(Label("Algorithm")); form.Children.Add(algo); form.Children.Add(Label("Digits")); form.Children.Add(digits); form.Children.Add(Label("Type")); form.Children.Add(type); form.Children.Add(Label("Period (TOTP)")); form.Children.Add(period);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = edit == null ? "Add account" : "Edit account", Content = form, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var account = new Account { Id = edit?.Id ?? Guid.NewGuid(), Counter = edit?.Counter ?? 0, Favorite = edit?.Favorite ?? false,
                Issuer = issuer.Text.Trim(), Name = name.Text.Trim(), Secret = secret.Password.Trim(), Algorithm = (string)algo.SelectedItem,
                Digits = int.Parse((string)digits.SelectedItem), Period = int.Parse(period.Text), Type = (string)type.SelectedItem };
            if (account.Issuer.Length == 0) throw new FormatException("Enter a service name.");
            _ = Otp.Generate(account);
            if (edit == null && _accounts.Any(x => x.Secret.Replace("=", "").Equals(account.Secret.Replace("=", ""), StringComparison.OrdinalIgnoreCase) && x.Issuer == account.Issuer && x.Name == account.Name))
                throw new FormatException("This account already exists.");
            var updated = _accounts.ToList();
            if (edit is null) updated.Add(account); else updated[updated.IndexOf(edit)] = account;
            _vault.Save(updated); _accounts = updated; RenderAccounts();
        }
        catch (Exception) { await ShowError("Account not saved", "Check the service, Base32 secret, algorithm and period, and ensure the vault is writable. Your existing account has not been changed."); }
        finally { secret.Password = ""; }
    }

    private async void Add_Click(object sender, RoutedEventArgs e) => await EditAccount();
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_loadFailed || _locked) return;
        var text = new TextBox { PlaceholderText = "otpauth:// or otpauth-migration:// URI", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 86 };
        var choose = new Button { Content = "Open QR image…", HorizontalAlignment = HorizontalAlignment.Stretch };
        var pasteImage = new Button { Content = "Paste QR image", HorizontalAlignment = HorizontalAlignment.Stretch };
        var backupFile = new Button { Content = "Open backup file…", HorizontalAlignment = HorizontalAlignment.Stretch };
        var status = new TextBlock { Text = "Choose an image or backup, or paste an otpauth URI.", TextWrapping = TextWrapping.Wrap };
        choose.Click += async (_, _) => { try { text.Text = await ScanFile() ?? text.Text; status.Text = "Image processed."; } catch { status.Text = "Cannot read the QR image. Choose a clear image under 25 MB and 16 megapixels."; } };
        pasteImage.Click += async (_, _) => { try { text.Text = await ScanClipboardImage() ?? text.Text; status.Text = "Clipboard image processed."; } catch { status.Text = "Cannot read a QR image from the clipboard."; } };
        backupFile.Click += async (_, _) => { try { text.Text = await ReadBackupFile() ?? text.Text; status.Text = "File selected."; } catch { status.Text = "Cannot open the backup. Check access and the 20 MB size limit."; } };
        var content = new StackPanel { Spacing = 9 }; content.Children.Add(status); content.Children.Add(choose); content.Children.Add(pasteImage); content.Children.Add(backupFile); content.Children.Add(text);
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Import accounts", Content = content, PrimaryButtonText = "Continue", CloseButtonText = "Cancel" };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var input = text.Text.Trim(); text.Text = "";
            if (input.Length > 20_000_000) throw new FormatException("Import exceeds the 20 MB limit.");
            List<Account> imported;
            if (input.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase)) imported = [OtpUriParser.Parse(input)];
            else if (input.StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase)) imported = MigrationParser.ParseUri(input);
            else if (input.StartsWith("NOIRPASS1:")) { var password = await AskPassword("Backup password"); if (password is null) return; var plain = DecryptBackup(Convert.FromBase64String(input[10..]), password); try { imported = System.Text.Json.JsonSerializer.Deserialize<List<Account>>(plain) ?? []; } finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain); } }
            else if (input.StartsWith("NOIRAUTH1:")) { var raw = Convert.FromBase64String(input[10..]); try { var plain = System.Security.Cryptography.ProtectedData.Unprotect(raw, null, System.Security.Cryptography.DataProtectionScope.CurrentUser); try { imported = System.Text.Json.JsonSerializer.Deserialize<List<Account>>(plain) ?? []; } finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain); } } finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(raw); } }
            else if (input.Length > 0 && input.TrimStart().StartsWith('{')) imported = await ParseAegis(input);
            else throw new FormatException("Choose a supported backup or paste an OTP URI.");
            await PreviewImport(imported);
        }
        catch (Exception) { await ShowError("Import failed", "Check the file format, account parameters and password. Import supports up to 500 accounts and 8 Aegis key slots."); }
    }
    private async Task<string?> ScanFile()
    {
        var picker = new FileOpenPicker(); foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }) picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync(); if (file == null) return null;
        if ((await file.GetBasicPropertiesAsync()).Size > 25 * 1024 * 1024) throw new FormatException("Изображение больше допустимого размера 25 МБ.");
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read); return await DecodeImage(stream);
    }
    private async Task<string?> ReadBackupFile()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json"); picker.FileTypeFilter.Add(".txt"); picker.FileTypeFilter.Add(".winotp");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return null;
        if ((await file.GetBasicPropertiesAsync()).Size > 20 * 1024 * 1024) throw new FormatException("Файл импорта больше допустимого размера 20 МБ.");
        return await Windows.Storage.FileIO.ReadTextAsync(file);
    }
    private static async Task<string?> ScanClipboardImage()
    {
        var view = Clipboard.GetContent(); if (!view.Contains(StandardDataFormats.Bitmap)) throw new FormatException("В буфере нет изображения.");
        var reference = await view.GetBitmapAsync(); using var stream = await reference.OpenReadAsync(); return await DecodeImage(stream);
    }
    private static async Task<string?> DecodeImage(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || (ulong)decoder.PixelWidth * decoder.PixelHeight > 16_000_000)
            throw new FormatException("Изображение QR слишком большое для безопасной обработки (максимум 16 мегапикселей).");
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage); var bytes = pixels.DetachPixelData();
        try
        {
            var reader = new BarcodeReaderGeneric { AutoRotate = true, Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] } };
            var result = reader.Decode(bytes, (int)decoder.PixelWidth, (int)decoder.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32) ?? throw new FormatException("На изображении не найден QR-код. Попробуйте выбрать более чёткую картинку.");
            return result.Text;
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
    private async Task PreviewImport(List<Account> imported)
    {
        if (imported.Count == 0) throw new FormatException("Формат не содержит аккаунтов.");
        if (imported.Count > 500) throw new FormatException("В одном импорте можно добавить не более 500 аккаунтов.");
        foreach (var account in imported)
        {
            if (account is null) throw new FormatException("Импорт содержит пустую запись аккаунта.");
            Otp.Validate(account);
            account.Id = Guid.NewGuid();
        }
        var rows = new StackPanel { Spacing = 6, MaxHeight = 360 };
        var choices = new List<(Account Account, CheckBox Check)>();
        foreach (var account in imported)
        {
            bool duplicate = _accounts.Any(x => x.Issuer == account.Issuer && x.Name == account.Name && Otp.DecodeBase32(x.Secret).SequenceEqual(Otp.DecodeBase32(account.Secret)));
            var check = new CheckBox { IsChecked = !duplicate, IsEnabled = !duplicate, Content = $"{account.Issuer}   ·   {account.Name}" };
            if (duplicate) check.Content += "   (already added)";
            choices.Add((account, check)); rows.Children.Add(check);
        }
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = $"Review import · {imported.Count} accounts", Content = new ScrollViewer { Content = rows, MaxHeight = 360 }, PrimaryButtonText = "Add selected", CloseButtonText = "Cancel" };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        var updated = _accounts.Concat(choices.Where(choice => choice.Check.IsChecked == true).Select(choice => choice.Account)).ToList();
        _vault.Save(updated); _accounts = updated; RenderAccounts();
    }
    private async Task<List<Account>> ParseAegis(string json)
    {
        string? password = null;
        if (AegisImporter.RequiresPassword(json)) { password = await AskPassword("Aegis backup password"); if (password is null) return []; }
        return AegisImporter.Parse(json, password);
    }
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (_loadFailed || _locked) return;
        try
        {
            var password = await AskPassword("Backup password"); if (password is null) return;
            if (password.Length < 8) { await ShowError("Password too short", "Use at least 8 characters."); return; }
            var picker = new FileSavePicker { SuggestedFileName = $"WinOTP-backup-{DateTime.Now:yyyy-MM-dd}" };
            picker.FileTypeChoices.Add("Encrypted WinOTP backup", new List<string> { ".winotp" });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            var raw = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_accounts);
            try
            {
                var data = "NOIRPASS1:" + Convert.ToBase64String(EncryptBackup(raw, password));
                await Windows.Storage.FileIO.WriteTextAsync(file, data);
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(raw); }
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Backup saved", Content = "Keep the encrypted file and its password in separate safe places.", CloseButtonText = "OK" }.ShowAsync();
        }
        catch (Exception) { await ShowError("Backup not saved", "Check access to the destination and try again."); }
    }
    private async Task<string?> AskPassword(string title)
    {
        var box = PasswordInput("Password", 300); var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = box, PrimaryButtonText = "Continue", CloseButtonText = "Cancel" };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? box.Password : null;
    }
    private static byte[] EncryptBackup(byte[] plain, string password)
    {
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16); var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var ciphertext = new byte[plain.Length]; var key = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        try { using var aes = new System.Security.Cryptography.AesGcm(key, 16); aes.Encrypt(nonce, plain, ciphertext, tag); return [.. salt, .. nonce, .. tag, .. ciphertext]; }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
    }
    private static byte[] DecryptBackup(byte[] blob, string password)
    {
        if (blob.Length < 44) throw new FormatException("Файл резервной копии повреждён.");
        var salt = blob.AsSpan(0, 16); var nonce = blob.AsSpan(16, 12); var tag = blob.AsSpan(28, 16); var ciphertext = blob.AsSpan(44); var plain = new byte[ciphertext.Length]; var key = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        try { using var aes = new System.Security.Cryptography.AesGcm(key, 16); aes.Decrypt(nonce, ciphertext, tag, plain); return plain; }
        catch (System.Security.Cryptography.CryptographicException) { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain); throw new FormatException("Пароль неверен или резервная копия повреждена."); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); System.Security.Cryptography.CryptographicOperations.ZeroMemory(blob); }
    }
    private void AddIfNew(Account account)
    {
        var key = Otp.DecodeBase32(account.Secret);
        if (_accounts.Any(x => x.Issuer == account.Issuer && x.Name == account.Name && Otp.DecodeBase32(x.Secret).SequenceEqual(key))) return;
        _accounts.Add(account);
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => RenderAccounts();
    private void UpdateCodes()
    {
        var seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var visibilityChanged = _renderedHideCodes != _settings.HideCodes;
        foreach (var account in _accounts)
        {
            if (!_cardLabels.TryGetValue(account.Id, out var labels)) continue;
            try
            {
                var isHotp = string.Equals(account.Type, "HOTP", StringComparison.OrdinalIgnoreCase);
                var step = isHotp ? account.Counter : account.Period > 0 ? seconds / account.Period : long.MinValue;
                if (visibilityChanged || !_codeSteps.TryGetValue(account.Id, out var previousStep) || step != previousStep)
                {
                    labels.Code.Text = _settings.HideCodes ? new string('•', account.Digits is 6 or 8 ? account.Digits : 6) : Otp.Generate(account);
                    _codeSteps[account.Id] = step;
                }
                labels.Time.Text = isHotp ? "Updated on copy" : account.Period > 0 ? $"Refreshes in {account.Period - seconds % account.Period}s" : "Invalid TOTP period";
            }
            catch (Exception) { labels.Code.Text = "••••••"; labels.Time.Text = "Invalid account parameters"; }
        }
        _renderedHideCodes = _settings.HideCodes;
    }
    private bool _renderedHideCodes = true;
    private void ShowCodes_Click(object sender, RoutedEventArgs e) { _settings.HideCodes = !_settings.HideCodes; _settings.Save(); ShowCodesButton.Content = _settings.HideCodes ? "Show codes" : "Hide codes"; UpdateCodes(); }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        Touch(); var hide = new CheckBox { Content = "Hide OTP codes", IsChecked = _settings.HideCodes };
        var clear = new ComboBox { ItemsSource = new[] { "Never", "15 seconds", "30 seconds", "60 seconds" }, SelectedIndex = _settings.ClipboardClearSeconds switch { 15 => 1, 30 => 2, 60 => 3, _ => 0 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var idle = new ComboBox { ItemsSource = new[] { "Never", "1 minute", "5 minutes", "10 minutes", "15 minutes" }, SelectedIndex = _settings.AutoLockMinutes switch { 1 => 1, 5 => 2, 10 => 3, 15 => 4, _ => 0 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var pass = PasswordInput(_settings.HasPassword ? "New password (leave blank to keep)" : "Master password (optional)", 340);
        var clearPass = new CheckBox { Content = "Remove master password", Visibility = _settings.HasPassword ? Visibility.Visible : Visibility.Collapsed };
        var form = new StackPanel { Spacing = 12 }; form.Children.Add(hide); form.Children.Add(Label("Clear clipboard after copying")); form.Children.Add(clear); form.Children.Add(Label("Lock after inactivity")); form.Children.Add(idle); form.Children.Add(Label("The master password locks the app interface")); form.Children.Add(pass); form.Children.Add(clearPass);
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Settings", Content = form, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        if (clearPass.IsChecked != true && !string.IsNullOrWhiteSpace(pass.Password) && pass.Password.Length < 8)
        {
            await ShowError("Password too short", "The master password must contain at least 8 characters.");
            return;
        }
        _settings.HideCodes = hide.IsChecked == true; _settings.ClipboardClearSeconds = clear.SelectedIndex switch { 1 => 15, 2 => 30, 3 => 60, _ => 0 }; _settings.AutoLockMinutes = idle.SelectedIndex switch { 1 => 1, 2 => 5, 3 => 10, 4 => 15, _ => 0 };
        if (clearPass.IsChecked == true) { _settings.PasswordSalt = null; _settings.PasswordHash = null; } else if (!string.IsNullOrWhiteSpace(pass.Password)) _settings.SetPassword(pass.Password);
        _settings.Save(); _lastActivity = DateTime.Now; ShowCodesButton.Content = _settings.HideCodes ? "Show codes" : "Hide codes"; RenderAccounts();
    }
    private void Touch() => _lastActivity = DateTime.Now;
    private async Task ClearClipboardAfter(string expected, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        try { var view = Clipboard.GetContent(); if (view.Contains(StandardDataFormats.Text) && await view.GetTextAsync() == expected) Clipboard.Clear(); } catch { }
    }
    private async Task LockAndUnlock()
    {
        _locked = true; _accounts = []; RenderAccounts(); await Unlock(); _locked = false; Touch(); RenderAccounts();
    }
    private async Task Unlock()
    {
        while (true)
        {
            var pass = PasswordInput("Master password", 280);
            var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Vault locked", Content = pass, PrimaryButtonText = "Unlock", DefaultButton = ContentDialogButton.Primary };
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary && _settings.VerifyPassword(pass.Password)) { _accounts = _vault.Load(); return; }
            if (result != ContentDialogResult.Primary) { Close(); return; }
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Incorrect password", Content = "Please try again.", CloseButtonText = "OK" }.ShowAsync();
        }
    }
    private void Save()
    {
        if (_loadFailed || _locked) throw new InvalidOperationException("Vault is unavailable.");
        _vault.Save(_accounts);
    }
    private static SolidColorBrush Brush(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#')
            return new(Microsoft.UI.ColorHelper.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16)));
        if (hex.Length == 9 && hex[0] == '#')
            return new(Microsoft.UI.ColorHelper.FromArgb(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16), Convert.ToByte(hex[7..9], 16)));
        throw new FormatException($"Неверный формат цвета: {hex}");
    }
    private static TextBox Input(string placeholder, string value) => new() { PlaceholderText = placeholder, Text = value, Background = Brush("#101010"), BorderBrush = Brush("#3A3A3A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10) };
    private static PasswordBox PasswordInput(string placeholder, double minWidth) => new() { PlaceholderText = placeholder, MinWidth = minWidth, RequestedTheme = ElementTheme.Dark, Background = Brush("#101010"), Foreground = Brush("#F0F0F0"), BorderBrush = Brush("#3A3A3A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10) };
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, Foreground = Brush("#777777") };
    private async Task ShowError(string title, string message) { await new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = message, CloseButtonText = "OK" }.ShowAsync(); }
}
