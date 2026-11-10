using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using NoirAuth.Models;
using NoirAuth.Services;

namespace NoirAuth;

public sealed partial class MainWindow : Window
{
    private readonly Vault _vault = new();
    private List<Account> _accounts;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Dictionary<Guid, (TextBlock Code, TextBlock Time)> _cardLabels = [];
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = null;
        _accounts = _vault.Load();
        _timer.Tick += (_, _) => { UpdateCodes(); ClockText.Text = DateTime.Now.ToString("HH:mm:ss"); };
        _timer.Start(); RenderAccounts();
    }

    private void RenderAccounts()
    {
        var q = SearchBox.Text.Trim();
        var visible = _accounts.Where(a => q.Length == 0 || a.Issuer.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Favorite).ThenBy(a => a.Issuer).ThenBy(a => a.Name).ToList();
        AccountList.Children.Clear(); _cardLabels.Clear();
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
        var remaining = a.Type == "HOTP" ? "ручное обновление" : $"обновится через {a.Period - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % a.Period} с";
        var title = new TextBlock { Text = a.Issuer, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("#EEEEEE") };
        var name = new TextBlock { Text = a.Name, FontSize = 12, Foreground = Brush("#777777") };
        var codeBlock = new TextBlock { Text = code, FontFamily = (FontFamily)Application.Current.Resources["MonoFont"], FontSize = 31, CharacterSpacing = 75, Foreground = error.Length > 0 ? Brush("#EF7777") : Brush("#F1F1F1"), VerticalAlignment = VerticalAlignment.Center };
        var metadata = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        metadata.Children.Add(title); metadata.Children.Add(name);
        var codeStack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        var timeBlock = new TextBlock { Text = error.Length > 0 ? error : remaining, FontSize = 10, Foreground = Brush("#727272"), HorizontalAlignment = HorizontalAlignment.Right };
        codeStack.Children.Add(codeBlock); codeStack.Children.Add(timeBlock);
        _cardLabels[a.Id] = (codeBlock, timeBlock);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var copy = new Button { Content = "Скопировать", Background = Brush("#171717"), Foreground = Brush("#CFCFCF"), CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 7, 10, 7), IsEnabled = error.Length == 0 };
        copy.Click += async (_, _) => { var p = new DataPackage(); p.SetText(code); Clipboard.SetContent(p); if (a.Type == "HOTP") { a.Counter++; Save(); RenderAccounts(); } copy.Content = "Скопировано"; await Task.Delay(1100); copy.Content = "Скопировать"; };
        var pin = new Button { Content = a.Favorite ? "★" : "☆", Background = Brush("#111111"), Foreground = Brush("#B7F36B"), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 6, 9, 6) };
        pin.Click += (_, _) => { a.Favorite = !a.Favorite; Save(); RenderAccounts(); };
        var edit = new Button { Content = "···", Background = Brush("#111111"), Foreground = Brush("#999999"), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 6, 9, 6) };
        edit.Click += async (_, _) => { var menu = new MenuFlyout(); menu.Items.Add(new MenuFlyoutItem { Text = "Изменить", Tag = "edit" }); menu.Items.Add(new MenuFlyoutItem { Text = "Удалить", Tag = "delete" }); menu.ItemClick += async (_, e) => { if ((string)((MenuFlyoutItem)e.ClickedItem).Tag! == "edit") await EditAccount(a); else { var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Удалить аккаунт?", Content = $"{a.Issuer} · {a.Name}", PrimaryButtonText = "Удалить", CloseButtonText = "Отмена", DefaultButton = ContentDialogButton.Close }; if (await dlg.ShowAsync() == ContentDialogResult.Primary) { _accounts.Remove(a); Save(); RenderAccounts(); } } }; menu.ShowAt(edit); };
        actions.Children.Add(copy); actions.Children.Add(pin); actions.Children.Add(edit);
        var row = new Grid { ColumnSpacing = 18, Margin = new Thickness(16, 13, 13, 13) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(metadata); Grid.SetColumn(codeStack, 1); row.Children.Add(codeStack); Grid.SetColumn(actions, 2); row.Children.Add(actions);
        return new Border { Background = Brush("#0A0A0A"), CornerRadius = new CornerRadius(12), Child = row };
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
        var text = new TextBox { PlaceholderText = "otpauth:// URI или резервная копия Noir Auth", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 };
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Импорт аккаунта", Content = text, PrimaryButtonText = "Импортировать", CloseButtonText = "Отмена" };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var input = text.Text.Trim();
            if (input.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase)) { AddIfNew(ParseOtpUri(input)); }
            else if (input.StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase)) { foreach (var a in ParseMigration(input)) AddIfNew(a); }
            else if (input.StartsWith("NOIRAUTH1:")) { var raw = Convert.FromBase64String(input[10..]); var plain = System.Security.Cryptography.ProtectedData.Unprotect(raw, null, System.Security.Cryptography.DataProtectionScope.CurrentUser); var imported = System.Text.Json.JsonSerializer.Deserialize<List<Account>>(plain) ?? []; foreach (var a in imported) if (!_accounts.Any(x => x.Issuer == a.Issuer && x.Name == a.Name && x.Secret == a.Secret)) _accounts.Add(a); }
            else throw new FormatException("Вставьте otpauth URI, Google Authenticator migration URI или файл резервной копии Noir Auth.");
            Save(); RenderAccounts();
        }
        catch (Exception ex) { await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Не удалось импортировать", Content = ex.Message, CloseButtonText = "ОК" }.ShowAsync(); }
    }
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var raw = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_accounts); var encrypted = System.Security.Cryptography.ProtectedData.Protect(raw, null, System.Security.Cryptography.DataProtectionScope.CurrentUser); var data = "NOIRAUTH1:" + Convert.ToBase64String(encrypted);
        var dlg = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Зашифрованная резервная копия", Content = new TextBox { Text = data, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 110 }, PrimaryButtonText = "Скопировать", CloseButtonText = "Закрыть" };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary) { var p = new DataPackage(); p.SetText(data); Clipboard.SetContent(p); }
    }
    private static Account ParseOtpUri(string uri)
    {
        var u = new Uri(uri); var label = Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/')); var parts = label.Split(':', 2); var query = System.Web.HttpUtility.ParseQueryString(u.Query);
        return new Account { Type = u.Host.Equals("hotp", StringComparison.OrdinalIgnoreCase) ? "HOTP" : "TOTP", Issuer = query["issuer"] ?? (parts.Length > 1 ? parts[0] : "Account"), Name = parts.Length > 1 ? parts[1] : parts[0], Secret = query["secret"] ?? throw new FormatException("В URI отсутствует secret."), Algorithm = (query["algorithm"] ?? "SHA1").ToUpperInvariant(), Digits = int.TryParse(query["digits"], out var d) ? d : 6, Period = int.TryParse(query["period"], out var p) ? p : 30 };
    }
    private void AddIfNew(Account account)
    {
        var key = Otp.DecodeBase32(account.Secret);
        if (_accounts.Any(x => x.Issuer == account.Issuer && x.Name == account.Name && Otp.DecodeBase32(x.Secret).SequenceEqual(key))) return;
        _accounts.Add(account);
    }
    private static List<Account> ParseMigration(string uri)
    {
        var u = new Uri(uri); var query = System.Web.HttpUtility.ParseQueryString(u.Query);
        var payload = query["data"] ?? throw new FormatException("В migration URI отсутствует параметр data.");
        using var stream = new MemoryStream(Convert.FromBase64String(payload.Replace(' ', '+')));
        var result = new List<Account>();
        while (stream.Position < stream.Length)
        {
            var tag = ReadVarint(stream); var field = (int)(tag >> 3); var wire = (int)(tag & 7);
            if (field == 1 && wire == 2) { var length = (int)ReadVarint(stream); var bytes = new byte[length]; stream.ReadExactly(bytes); result.Add(ParseMigrationEntry(bytes)); }
            else SkipField(stream, wire);
        }
        if (result.Count == 0) throw new FormatException("В migration QR не найдено аккаунтов.");
        return result;
    }
    private static Account ParseMigrationEntry(byte[] bytes)
    {
        using var s = new MemoryStream(bytes); byte[] secret = []; string name = "", issuer = ""; int algorithm = 1, digits = 1, type = 2; long counter = 0;
        while (s.Position < s.Length)
        {
            var tag = ReadVarint(s); var field = (int)(tag >> 3); var wire = (int)(tag & 7);
            if (wire == 2)
            {
                var length = (int)ReadVarint(s); var data = new byte[length]; s.ReadExactly(data);
                if (field == 1) secret = data; else if (field == 2) name = System.Text.Encoding.UTF8.GetString(data); else if (field == 3) issuer = System.Text.Encoding.UTF8.GetString(data);
            }
            else if (wire == 0)
            {
                var value = ReadVarint(s);
                if (field == 4) algorithm = (int)value; else if (field == 5) digits = (int)value; else if (field == 6) type = (int)value; else if (field == 7) counter = (long)value;
            }
            else SkipField(s, wire);
        }
        if (secret.Length == 0) throw new FormatException("Migration-аккаунт не содержит секрет.");
        var label = name.Split(':', 2); var resolvedIssuer = issuer.Length > 0 ? issuer : (label.Length > 1 ? label[0] : "Account"); var resolvedName = label.Length > 1 ? label[1] : label[0];
        return new Account { Issuer = resolvedIssuer, Name = resolvedName, Secret = Base32(secret), Algorithm = algorithm switch { 2 => "SHA256", 3 => "SHA512", _ => "SHA1" }, Digits = digits == 2 ? 8 : 6, Type = type == 1 ? "HOTP" : "TOTP", Period = 30, Counter = counter };
    }
    private static string Base32(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; var sb = new System.Text.StringBuilder(); int buffer = 0, bits = 0;
        foreach (var b in bytes) { buffer = (buffer << 8) | b; bits += 8; while (bits >= 5) { bits -= 5; sb.Append(alphabet[(buffer >> bits) & 31]); } }
        if (bits > 0) sb.Append(alphabet[(buffer << (5 - bits)) & 31]); return sb.ToString();
    }
    private static ulong ReadVarint(Stream s)
    {
        ulong value = 0; int shift = 0; while (shift < 70) { int b = s.ReadByte(); if (b < 0) throw new FormatException("Повреждён protobuf migration QR."); value |= (ulong)(b & 127) << shift; if ((b & 128) == 0) return value; shift += 7; }
        throw new FormatException("Некорректный protobuf varint.");
    }
    private static void SkipField(Stream s, int wire)
    {
        switch (wire) { case 0: _ = ReadVarint(s); break; case 1: s.Seek(8, SeekOrigin.Current); break; case 2: s.Seek((long)ReadVarint(s), SeekOrigin.Current); break; case 5: s.Seek(4, SeekOrigin.Current); break; default: throw new FormatException("Неизвестный protobuf wire type."); }
        if (s.Position > s.Length) throw new FormatException("Обрезанный migration payload.");
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => RenderAccounts();
    private void UpdateCodes()
    {
        var seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var account in _accounts)
        {
            if (!_cardLabels.TryGetValue(account.Id, out var labels)) continue;
            try { labels.Code.Text = Otp.Generate(account); labels.Time.Text = account.Type == "HOTP" ? "ручное обновление" : $"обновится через {account.Period - seconds % account.Period} с"; }
            catch (Exception e) { labels.Code.Text = "••••••"; labels.Time.Text = e.Message; }
        }
    }
    private void Save() => _vault.Save(_accounts);
    private static SolidColorBrush Brush(string hex) => new(Microsoft.UI.ColorHelper.FromArgb(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16), Convert.ToByte(hex[7..9], 16)));
    private static TextBox Input(string placeholder, string value) => new() { PlaceholderText = placeholder, Text = value, Background = Brush("#111111"), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 8, 10, 8) };
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, Foreground = Brush("#777777") };
}
