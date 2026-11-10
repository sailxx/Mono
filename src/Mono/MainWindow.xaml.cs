using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
using Mono.Models;
using Mono.Services;

namespace Mono;

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
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = null;
        var windowHandle = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle));
        var titleBar = appWindow.TitleBar;
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
        appWindow.Resize(new SizeInt32(620, 640));
        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = version is null ? "v0.3.0" : $"v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        ShowCodesButton.Content = _settings.HideCodes ? "Показать коды" : "Скрыть коды";
        if (Content is UIElement root) { root.PointerPressed += (_, _) => Touch(); root.KeyDown += (_, _) => Touch(); }
        _timer.Tick += async (_, _) =>
        {
            try
            {
                ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
                if (!_locked && _settings.HasPassword && _settings.AutoLockMinutes > 0 && DateTime.Now - _lastActivity >= TimeSpan.FromMinutes(_settings.AutoLockMinutes)) await LockAndUnlock();
                else UpdateCodes();
            }
            catch (Exception ex) { LogStartup($"Timer action failed: {ex}"); }
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
                LogStartup($"Initial screen failed: {ex}");
                AccountList.Children.Clear();
                AccountList.Children.Add(new TextBlock
                {
                    Text = $"Не удалось загрузить хранилище: {ex.Message}",
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
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mono");
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
        CountText.Text = $"{_accounts.Count} АККАУНТОВ · DPAPI";
        foreach (var account in visible) AccountList.Children.Add(BuildCard(account));
        if (visible.Count == 0)
        {
            var empty = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 100, 0, 0) };
            empty.Children.Add(new TextBlock { Text = _accounts.Count == 0 ? "Ваши коды будут здесь" : "Ничего не найдено", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            empty.Children.Add(new TextBlock { Text = "Добавьте аккаунт или импортируйте резервную копию", Foreground = Brush("#666666"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
            AccountList.Children.Add(empty);
        }
    }

    private Border BuildCard(Account a)
    {
        string code = "••••••"; string error = "";
        try { code = Otp.Generate(a); } catch (Exception e) { error = e.Message; }
        var isHotp = string.Equals(a.Type, "HOTP", StringComparison.OrdinalIgnoreCase);
        var remaining = isHotp ? "ручное обновление" : a.Period > 0 ? $"обновится через {a.Period - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % a.Period} с" : "некорректный период TOTP";
        var title = new TextBlock { Text = a.Issuer, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("#EEEEEE") };
        var name = new TextBlock { Text = a.Name, FontSize = 12, Foreground = Brush("#777777") };
        var codeBlock = new TextBlock { Text = _settings.HideCodes ? new string('•', a.Digits) : code, FontFamily = new FontFamily("ms-appx:///Assets/Fonts/JetBrainsMono-Regular.ttf#JetBrains Mono"), FontSize = 25, CharacterSpacing = 50, Foreground = error.Length > 0 ? Brush("#B0B0B0") : Brush("#F1F1F1"), VerticalAlignment = VerticalAlignment.Center };
        var metadata = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        metadata.Children.Add(title); metadata.Children.Add(name);
        var codeStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var timeBlock = new TextBlock { Text = error.Length > 0 ? error : remaining, FontSize = 9, Foreground = Brush("#727272") };
        codeStack.Children.Add(codeBlock); codeStack.Children.Add(timeBlock);
        _cardLabels[a.Id] = (codeBlock, timeBlock);
        _codeSteps[a.Id] = isHotp ? a.Counter : a.Period > 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() / a.Period : long.MinValue;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var copy = new Button { Content = "Копировать", Background = Brush("#171717"), Foreground = Brush("#CFCFCF"), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 6, 9, 6), IsEnabled = error.Length == 0, FontFamily = new FontFamily("ms-appx:///Assets/Fonts/JetBrainsMono-Regular.ttf#JetBrains Mono") };
        copy.Click += async (_, _) =>
        {
            Touch();
            try
            {
                var current = Otp.Generate(a);
                if (isHotp)
                {
                    if (a.Counter == long.MaxValue) throw new InvalidOperationException("Счётчик HOTP достиг максимального значения.");
                    a.Counter++;
                    try { Save(); } catch { a.Counter--; throw; }
                    RenderAccounts();
                }
                var package = new DataPackage(); package.SetText(current); Clipboard.SetContent(package);
                if (_settings.ClipboardClearSeconds > 0) _ = ClearClipboardAfter(current, _settings.ClipboardClearSeconds);
                copy.Content = "Скопировано"; await Task.Delay(1100); copy.Content = "Копировать";
            }
            catch (Exception ex) { await ShowError("Код не скопирован", ex.Message); }
        };
        var pin = new Button { Content = a.Favorite ? "★" : "☆", Background = Brush("#111111"), Foreground = Brush("#FFFFFF"), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 5, 8, 5) };
        pin.Click += (_, _) => { a.Favorite = !a.Favorite; Save(); RenderAccounts(); };
        var edit = new Button { Content = "···", Background = Brush("#111111"), Foreground = Brush("#999999"), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 5, 8, 5) };
        edit.Click += (_, _) => { var menu = new MenuFlyout(); var editItem = new MenuFlyoutItem { Text = "Изменить" }; editItem.Click += async (_, _) => await EditAccount(a); var deleteItem = new MenuFlyoutItem { Text = "Удалить" }; deleteItem.Click += async (_, _) => { var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Удалить аккаунт?", Content = $"{a.Issuer} · {a.Name}", PrimaryButtonText = "Удалить", CloseButtonText = "Отмена", DefaultButton = ContentDialogButton.Close }; if (await dlg.ShowAsync() == ContentDialogResult.Primary) { _accounts.Remove(a); Save(); RenderAccounts(); } }; menu.Items.Add(editItem); menu.Items.Add(deleteItem); menu.ShowAt(edit); };
        actions.Children.Add(pin); actions.Children.Add(edit);
        var top = new Grid { ColumnSpacing = 8 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(metadata); Grid.SetColumn(actions, 1); top.Children.Add(actions);
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(12, 10, 12, 10) };
        body.Children.Add(top); body.Children.Add(codeStack);
        var copyRow = new StackPanel { Orientation = Orientation.Horizontal };
        copyRow.Children.Add(copy); body.Children.Add(copyRow);
        return new Border { Background = Brush("#090909"), BorderBrush = Brush("#202020"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = body };
    }

    private async Task EditAccount(Account? edit = null)
    {
        var issuer = Input("Сервис", edit?.Issuer ?? ""); var name = Input("Аккаунт / email", edit?.Name ?? ""); var secret = Input("Secret Key (Base32)", edit?.Secret ?? "");
        var algo = new ComboBox { ItemsSource = new[] { "SHA1", "SHA256", "SHA512" }, SelectedItem = edit?.Algorithm ?? "SHA1", HorizontalAlignment = HorizontalAlignment.Stretch };
        var digits = new ComboBox { ItemsSource = new[] { "6", "8" }, SelectedItem = (edit?.Digits ?? 6).ToString(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var period = new ComboBox { ItemsSource = new[] { "30", "60" }, SelectedItem = (edit?.Period ?? 30).ToString(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var type = new ComboBox { ItemsSource = new[] { "TOTP", "HOTP" }, SelectedItem = edit?.Type ?? "TOTP", HorizontalAlignment = HorizontalAlignment.Stretch };
        var form = new StackPanel { Spacing = 11, Width = 380 }; form.Children.Add(issuer); form.Children.Add(name); form.Children.Add(secret);
        form.Children.Add(Label("Алгоритм")); form.Children.Add(algo); form.Children.Add(Label("Разрядность")); form.Children.Add(digits); form.Children.Add(Label("Тип")); form.Children.Add(type); form.Children.Add(Label("Период (TOTP)")); form.Children.Add(period);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = edit == null ? "Новый аккаунт" : "Изменить аккаунт", Content = form, PrimaryButtonText = "Сохранить", CloseButtonText = "Отмена", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var account = edit ?? new Account(); account.Issuer = issuer.Text.Trim(); account.Name = name.Text.Trim(); account.Secret = secret.Text.Trim(); account.Algorithm = (string)algo.SelectedItem; account.Digits = int.Parse((string)digits.SelectedItem); account.Period = int.Parse((string)period.SelectedItem); account.Type = (string)type.SelectedItem;
            if (account.Issuer.Length == 0) throw new FormatException("Укажите название сервиса."); _ = Otp.Generate(account);
            if (edit == null) { var duplicate = _accounts.Any(x => x.Secret.Replace("=", "").Equals(account.Secret.Replace("=", ""), StringComparison.OrdinalIgnoreCase) && x.Issuer == account.Issuer && x.Name == account.Name); if (duplicate) throw new FormatException("Такой аккаунт уже добавлен."); _accounts.Add(account); }
            Save(); RenderAccounts();
        }
        catch (Exception e) { await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Проверьте данные", Content = e.Message, CloseButtonText = "ОК" }.ShowAsync(); }
    }

    private async void Add_Click(object sender, RoutedEventArgs e) => await EditAccount();
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var text = new TextBox { PlaceholderText = "otpauth:// или otpauth-migration:// URI", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 86 };
        var choose = new Button { Content = "Загрузить QR-изображение…", Background = Brush("#171717"), Foreground = Brush("#E0E0E0"), HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10) };
        var pasteImage = new Button { Content = "Вставить QR-изображение из буфера", Background = Brush("#171717"), Foreground = Brush("#E0E0E0"), HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10) };
        var backupFile = new Button { Content = "Выбрать файл резервной копии…", Background = Brush("#171717"), Foreground = Brush("#E0E0E0"), HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10) };
        string? scanned = null;
        choose.Click += async (_, _) => { try { scanned = await ScanFile(); if (scanned != null) text.Text = scanned; } catch (Exception ex) { await ShowError("QR не прочитан", ex.Message); } };
        pasteImage.Click += async (_, _) => { try { scanned = await ScanClipboardImage(); if (scanned != null) text.Text = scanned; } catch (Exception ex) { await ShowError("QR не прочитан", ex.Message); } };
        backupFile.Click += async (_, _) => { try { text.Text = await ReadBackupFile() ?? text.Text; } catch (Exception ex) { await ShowError("Файл не открыт", ex.Message); } };
        var content = new StackPanel { Spacing = 9 }; content.Children.Add(new TextBlock { Text = "Выберите QR-картинку, файл Aegis JSON или вставьте otpauth URI.", Foreground = Brush("#8B8B8B"), TextWrapping = TextWrapping.Wrap }); content.Children.Add(choose); content.Children.Add(pasteImage); content.Children.Add(backupFile); content.Children.Add(text);
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Импорт аккаунтов", Content = content, PrimaryButtonText = "Продолжить", CloseButtonText = "Отмена" };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var input = text.Text.Trim();
            if (input.Length > 20_000_000) throw new FormatException("Файл импорта слишком большой (лимит 20 МБ).");
            List<Account> imported;
            if (input.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase)) imported = [OtpUriParser.Parse(input)];
            else if (input.StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase)) imported = MigrationParser.ParseUri(input);
            else if (input.StartsWith("NOIRPASS1:")) { var password = await AskPassword("Пароль резервной копии"); if (password is null) return; var plain = DecryptBackup(Convert.FromBase64String(input[10..]), password); try { imported = System.Text.Json.JsonSerializer.Deserialize<List<Account>>(plain) ?? []; } finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain); } }
            else if (input.StartsWith("NOIRAUTH1:")) { var raw = Convert.FromBase64String(input[10..]); var plain = System.Security.Cryptography.ProtectedData.Unprotect(raw, null, System.Security.Cryptography.DataProtectionScope.CurrentUser); try { imported = System.Text.Json.JsonSerializer.Deserialize<List<Account>>(plain) ?? []; } finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain); System.Security.Cryptography.CryptographicOperations.ZeroMemory(raw); } }
            else if (input.Length > 0 && input.TrimStart().StartsWith('{')) imported = await ParseAegis(input);
            else throw new FormatException("Вставьте otpauth URI, Google Authenticator migration URI или файл резервной копии Mono.");
            await PreviewImport(imported);
        }
        catch (Exception ex) { await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Не удалось импортировать", Content = ex.Message, CloseButtonText = "ОК" }.ShowAsync(); }
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
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json"); picker.FileTypeFilter.Add(".txt");
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
            if (duplicate) check.Content += "   (уже есть)";
            choices.Add((account, check)); rows.Children.Add(check);
        }
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = $"Проверьте импорт · {imported.Count} записей", Content = new ScrollViewer { Content = rows, MaxHeight = 360 }, PrimaryButtonText = "Добавить выбранные", CloseButtonText = "Отмена" };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        foreach (var choice in choices) if (choice.Check.IsChecked == true) _accounts.Add(choice.Account);
        Save(); RenderAccounts();
    }
    private async Task<List<Account>> ParseAegis(string json)
    {
        string? password = null;
        if (AegisImporter.RequiresPassword(json)) { password = await AskPassword("Пароль резервной копии Aegis"); if (password is null) return []; }
        return AegisImporter.Parse(json, password);
    }
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var password = await AskPassword("Пароль для резервной копии"); if (password is null) return;
        if (password.Length < 8) { await ShowError("Слишком короткий пароль", "Используйте не менее 8 символов."); return; }
        var raw = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_accounts); string data;
        try { data = "NOIRPASS1:" + Convert.ToBase64String(EncryptBackup(raw, password)); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(raw); }
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Резервная копия готова", Content = new TextBox { Text = data, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 110 }, PrimaryButtonText = "Скопировать", CloseButtonText = "Закрыть" };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary) { var p = new DataPackage(); p.SetText(data); Clipboard.SetContent(p); _ = ClearClipboardAfter(data, 30); }
    }
    private async Task<string?> AskPassword(string title)
    {
        var box = new PasswordBox { PlaceholderText = "Не менее 8 символов", MinWidth = 300 }; var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = box, PrimaryButtonText = "Продолжить", CloseButtonText = "Отмена" };
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
                labels.Time.Text = isHotp ? "ручное обновление" : account.Period > 0 ? $"обновится через {account.Period - seconds % account.Period} с" : "некорректный период TOTP";
            }
            catch (Exception e) { labels.Code.Text = "••••••"; labels.Time.Text = e.Message; }
        }
        _renderedHideCodes = _settings.HideCodes;
    }
    private bool _renderedHideCodes = true;
    private void ShowCodes_Click(object sender, RoutedEventArgs e) { _settings.HideCodes = !_settings.HideCodes; _settings.Save(); ShowCodesButton.Content = _settings.HideCodes ? "Показать коды" : "Скрыть коды"; UpdateCodes(); }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        Touch(); var hide = new CheckBox { Content = "Скрывать OTP-коды при запуске", IsChecked = _settings.HideCodes };
        var clear = new ComboBox { ItemsSource = new[] { "Не очищать", "15 секунд", "30 секунд", "60 секунд" }, SelectedItem = _settings.ClipboardClearSeconds switch { 15 => "15 секунд", 30 => "30 секунд", 60 => "60 секунд", _ => "Не очищать" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var idle = new ComboBox { ItemsSource = new[] { "Не блокировать", "1 минута", "5 минут", "10 минут", "15 минут" }, SelectedItem = _settings.AutoLockMinutes switch { 1 => "1 минута", 5 => "5 минут", 10 => "10 минут", 15 => "15 минут", _ => "Не блокировать" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var pass = new PasswordBox { PlaceholderText = _settings.HasPassword ? "Новый мастер-пароль (пусто — без изменений)" : "Мастер-пароль (необязательно)", MinWidth = 340 };
        var clearPass = new CheckBox { Content = "Удалить мастер-пароль", Visibility = _settings.HasPassword ? Visibility.Visible : Visibility.Collapsed };
        var form = new StackPanel { Spacing = 12 }; form.Children.Add(hide); form.Children.Add(Label("Очистка буфера после копирования")); form.Children.Add(clear); form.Children.Add(Label("Автоблокировка при бездействии")); form.Children.Add(idle); form.Children.Add(Label("Мастер-пароль защищает доступ к окну приложения")); form.Children.Add(pass); form.Children.Add(clearPass);
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Настройки", Content = form, PrimaryButtonText = "Сохранить", CloseButtonText = "Отмена" };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        if (clearPass.IsChecked != true && !string.IsNullOrWhiteSpace(pass.Password) && pass.Password.Length < 8)
        {
            await ShowError("Слишком короткий пароль", "Мастер-пароль должен содержать не менее 8 символов.");
            return;
        }
        _settings.HideCodes = hide.IsChecked == true; _settings.ClipboardClearSeconds = (string)clear.SelectedItem! switch { "15 секунд" => 15, "30 секунд" => 30, "60 секунд" => 60, _ => 0 }; _settings.AutoLockMinutes = (string)idle.SelectedItem! switch { "1 минута" => 1, "5 минут" => 5, "10 минут" => 10, "15 минут" => 15, _ => 0 };
        if (clearPass.IsChecked == true) { _settings.PasswordSalt = null; _settings.PasswordHash = null; } else if (!string.IsNullOrWhiteSpace(pass.Password)) _settings.SetPassword(pass.Password);
        _settings.Save(); _lastActivity = DateTime.Now; ShowCodesButton.Content = _settings.HideCodes ? "Показать коды" : "Скрыть коды"; RenderAccounts();
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
            var pass = new PasswordBox { PlaceholderText = "Мастер-пароль", MinWidth = 280 };
            var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Хранилище заблокировано", Content = pass, PrimaryButtonText = "Разблокировать", DefaultButton = ContentDialogButton.Primary };
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary && _settings.VerifyPassword(pass.Password)) { _accounts = _vault.Load(); return; }
            if (result != ContentDialogResult.Primary) { Close(); return; }
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Неверный пароль", Content = "Попробуйте ещё раз.", CloseButtonText = "ОК" }.ShowAsync();
        }
    }
    private void Save() => _vault.Save(_accounts);
    private static SolidColorBrush Brush(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#')
            return new(Microsoft.UI.ColorHelper.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16)));
        if (hex.Length == 9 && hex[0] == '#')
            return new(Microsoft.UI.ColorHelper.FromArgb(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16), Convert.ToByte(hex[7..9], 16)));
        throw new FormatException($"Неверный формат цвета: {hex}");
    }
    private static TextBox Input(string placeholder, string value) => new() { PlaceholderText = placeholder, Text = value, Background = Brush("#111111"), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 8, 10, 8) };
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, Foreground = Brush("#777777") };
    private async Task ShowError(string title, string message) { await new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = message, CloseButtonText = "ОК" }.ShowAsync(); }
}
