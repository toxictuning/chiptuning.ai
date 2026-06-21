using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ChiptuningAi.Client;
using ChiptuningAi.Client.BulkImport;
using ChiptuningAi.Client.Files;
using ChiptuningAi.Dashboard.Services;

namespace ChiptuningAi.Dashboard;

// ── Bulk import row (live-updates during upload) ──────────────────────────────
internal sealed class BulkRow : INotifyPropertyChanged
{
    private string _importStatus = string.Empty;

    public required string       FileName     { get; init; }
    public required string       Vehicle      { get; init; }
    public required string       Ecu          { get; init; }
    public          string?      Power        { get; init; }
    public          string?      ErrorText    { get; init; }
    public          string?      FilePath     { get; set;  }
    public required ParsedFileDto Parsed      { get; init; }

    public string ImportStatus
    {
        get => _importStatus;
        set { _importStatus = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImportStatus))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

file sealed record FileRow(
    Guid   FileId,
    string FileName,
    string Vehicle,
    string Ecu,
    string Read,
    long   SizeKb,
    int    PatchCount,
    string Uploaded);

public partial class MainWindow : Window
{
    private readonly ChiptuningAiClient _client;
    private string _apiUrl = string.Empty;
    private string _email  = string.Empty;
    private Guid _currentUserId;
    private string? _droppedFilePath;
    private IReadOnlyList<SimilarFile> _currentSimilarFiles = [];

    // ── Bulk import state ─────────────────────────────────────────────────────
    private bool _isBusiness;
    private readonly ObservableCollection<BulkRow> _bulkRows = [];
    private List<string> _pendingBulkPaths = [];

    // ── Match row ─────────────────────────────────────────────────────────────
    private sealed record MatchRow(Guid FileId, string FileName, string SimilarityPct);

    // ── Toast ─────────────────────────────────────────────────────────────────

    private sealed record ToastMessage(string Icon, string Message);
    private readonly ObservableCollection<ToastMessage> _toasts = [];

    private void ShowToast(string message, string icon = "ℹ")
    {
        var toast = new ToastMessage(icon, message);
        _toasts.Add(toast);
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.7) };
        t.Tick += (_, _) => { t.Stop(); _toasts.Remove(toast); };
        t.Start();
    }

    // ── Token refresh timer ───────────────────────────────────────────────────

    private DispatcherTimer? _refreshTimer;

    private void StartRefreshTimer()
    {
        _refreshTimer?.Stop();
        if (_client.RefreshToken is null) return;

        // When TokenExpiresAt is null (e.g. session restored from disk without expiry),
        // refresh immediately so the expiry is known before scheduling the next cycle.
        TimeSpan delay;
        if (_client.TokenExpiresAt is { } expiresAt)
        {
            delay = expiresAt - DateTimeOffset.UtcNow - TimeSpan.FromSeconds(60);
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        }
        else
        {
            delay = TimeSpan.FromSeconds(2);
        }

        _refreshTimer = new DispatcherTimer { Interval = delay };
        _refreshTimer.Tick += async (_, _) =>
        {
            _refreshTimer.Stop();
            try
            {
                await _client.Auth.RefreshAsync();
                // Persist new tokens so the next app start doesn't load stale credentials
                if (!string.IsNullOrEmpty(_apiUrl))
                    SessionStore.Save(_apiUrl, _email,
                        _client.AccessToken  ?? string.Empty,
                        _client.RefreshToken ?? string.Empty,
                        _client.TokenExpiresAt);
                StartRefreshTimer();
            }
            catch (Exception ex)
            {
                var code = AppLogger.Error("Token refresh failed", ex);
                SessionStore.Clear();
                ShowToast($"Session expired ({code})", "✕");
                new LoginWindow().Show();
                Close();
            }
        };
        _refreshTimer.Start();
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainWindow(ChiptuningAiClient client, string apiUrl = "", string email = "")
    {
        InitializeComponent();
        _client = client;
        _apiUrl = apiUrl;
        _email  = email;
        ToastList.ItemsSource  = _toasts;
        BulkGrid.ItemsSource   = _bulkRows;

        ThemeToggleBtn.Content = ThemeManager.IsDark ? "☾" : "☀";
        UpdateSwatchHighlight();

        Loaded += async (_, _) =>
        {
            InitLangPicker();
            await LoadProfileAsync();
            _ = LoadLookupsAsync();
            StartRefreshTimer();
        };

        ShowPanel("DropZone");
    }

    private void InitLangPicker()
    {
        foreach (var lang in LanguageManager.Languages)
        {
            var btn = new Button
            {
                Content     = BuildLangContent(lang.Flag, lang.Code),
                Tag         = lang.Code,
                Background  = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor      = Cursors.Hand,
                Padding     = new Thickness(8, 6, 8, 6),
                Margin      = new Thickness(2),
                ToolTip     = lang.Display,
            };
            btn.Click += LangPopupItem_Click;
            LangGrid.Children.Add(btn);
        }
        UpdateLangButton();
    }

    private static StackPanel BuildLangContent(string flag, string code)
    {
        var sp = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        sp.Children.Add(new TextBlock
        {
            Text                = flag,
            FontFamily          = new FontFamily("Segoe UI Emoji"),
            FontSize            = 20,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text                = code.ToUpperInvariant(),
            FontSize            = 9,
            FontWeight          = FontWeights.SemiBold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Foreground          = (Brush)Application.Current.Resources["TextSecondary"],
        });
        return sp;
    }

    private void LangBtn_Click(object sender, RoutedEventArgs e)
        => LangPopup.IsOpen = !LangPopup.IsOpen;

    private void LangPopupItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string code) return;
        LangPopup.IsOpen = false;
        LanguageManager.Apply(code);
        UpdateLangButton();
    }

    private void UpdateLangButton()
    {
        var lang = LanguageManager.Languages.FirstOrDefault(l => l.Code == LanguageManager.CurrentCode);
        LangBtnText.Text = lang.Flag ?? "🌐";
    }

    // ── Maximize (manual — avoids AllowsTransparency gap bug) ────────────────

    private bool _isManuallyMaximized;
    private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;

    private void ToggleMaximize()
    {
        if (_isManuallyMaximized)
        {
            Left   = _restoreLeft;
            Top    = _restoreTop;
            Width  = _restoreWidth;
            Height = _restoreHeight;
            _isManuallyMaximized = false;
            MaximizeBtn.Content = "☐";
        }
        else
        {
            _restoreLeft   = Left;
            _restoreTop    = Top;
            _restoreWidth  = Width;
            _restoreHeight = Height;
            var wa = SystemParameters.WorkArea;
            Left   = wa.Left;
            Top    = wa.Top;
            Width  = wa.Width;
            Height = wa.Height;
            _isManuallyMaximized = true;
            MaximizeBtn.Content = "❐";
        }
    }

    // ── Title bar ─────────────────────────────────────────────────────────────

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (e.LeftButton == MouseButtonState.Pressed && !_isManuallyMaximized)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        // Only handle minimise restore — maximize is managed manually above
        if (WindowState == WindowState.Normal && _isManuallyMaximized)
        {
            // Restored from taskbar click while maximised — re-apply manual max
            Dispatcher.BeginInvoke(() =>
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Left; Top = wa.Top;
                Width = wa.Width; Height = wa.Height;
                WindowState = WindowState.Normal;
            });
        }
    }

    // ── Theme / swatch ────────────────────────────────────────────────────────

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.SetTheme(!ThemeManager.IsDark);
        ThemeToggleBtn.Content = ThemeManager.IsDark ? "☾" : "☀";
        UpdateSwatchHighlight();
    }

    private void Swatch_Click(object sender, MouseButtonEventArgs e)
    {
        var key = ((Border)sender).Tag.ToString()!;
        ThemeManager.SetSwatch(key);
        UpdateSwatchHighlight();
    }

    private void UpdateSwatchHighlight()
    {
        var activeBrush = (Brush)Application.Current.Resources["TextPrimary"];
        var noBrush = Brushes.Transparent;
        var active  = ThemeManager.CurrentSwatch;

        SwatchMono.BorderBrush   = active == "Mono"   ? activeBrush : noBrush;
        SwatchBlue.BorderBrush   = active == "Blue"   ? activeBrush : noBrush;
        SwatchGreen.BorderBrush  = active == "Green"  ? activeBrush : noBrush;
        SwatchRed.BorderBrush    = active == "Red"    ? activeBrush : noBrush;
        SwatchPurple.BorderBrush = active == "Purple" ? activeBrush : noBrush;
        SwatchAmber.BorderBrush  = active == "Amber"  ? activeBrush : noBrush;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        var tag = ((Button)sender).Tag?.ToString() ?? "DropZone";
        ShowPanel(tag);
    }

    private void ShowPanel(string name)
    {
        PanelDropZone.Visibility    = name == "DropZone"    ? Visibility.Visible : Visibility.Collapsed;
        PanelFiles.Visibility       = name == "Files"       ? Visibility.Visible : Visibility.Collapsed;
        PanelUpload.Visibility      = name == "Upload"      ? Visibility.Visible : Visibility.Collapsed;
        PanelProfile.Visibility     = name == "Profile"     ? Visibility.Visible : Visibility.Collapsed;
        PanelBulkImport.Visibility  = name == "BulkImport" ? Visibility.Visible : Visibility.Collapsed;

        if (name == "Upload")  ResetUploadForm();
        if (name == "Files")   _ = LoadFilesAsync(silent: _filesLoaded);
        if (name == "Profile") _ = LoadProfileAsync();
    }

    private void ResetUploadForm()
    {
        UploadFilePath.Text = string.Empty;
        MdVehicleClass.Text  = string.Empty;
        MdVehicleMake.Text   = string.Empty;
        MdVehicleModel.Text  = string.Empty;
        MdVehicleVariant.Text = string.Empty;
        MdEngineType.Text    = string.Empty;
        MdECUType.Text       = string.Empty;
        MdECUMake.Text       = string.Empty;
        MdECUModel.Text      = string.Empty;
        MdReadHardware.Text  = string.Empty;
        MdReadMode.Text      = string.Empty;
        MdHWNumber.Text      = string.Empty;
        MdSWNumber.Text      = string.Empty;
        MdPower.Text         = string.Empty;
        MdTorque.Text        = string.Empty;
        UploadStatus.Text    = string.Empty;
        UploadProgress.Value = 0;
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        _refreshTimer?.Stop();
        SessionStore.Clear();
        try { await _client.Auth.LogoutAsync(); } catch { }
        new LoginWindow().Show();
        Close();
    }

    // ── Drop zone ─────────────────────────────────────────────────────────────

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            DropRect.Stroke = (Brush)Application.Current.Resources["DropZoneBorderHover"];
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        DropRect.Stroke = (Brush)Application.Current.Resources["DropZoneBorder"];
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropRect.Stroke = (Brush)Application.Current.Resources["DropZoneBorder"];
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length == 0) return;
        _ = RunDropSearchAsync(files[0]);
    }

    private void BrowseDropZone_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select ECU file",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true)
            _ = RunDropSearchAsync(dlg.FileName);
    }

    private async Task RunDropSearchAsync(string filePath)
    {
        // Check for an existing .ctatrace session alongside the dropped file
        var tracePath = TraceFile.GetTracePath(filePath);
        if (File.Exists(tracePath))
        {
            var trace = TraceFile.Load(tracePath);
            if (trace is not null)
            {
                var answer = MessageBox.Show(
                    $"A previous session log was found for this file.\n\nWould you like to continue from where you left off?",
                    "Resume Session?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes)
                {
                    var win = new ProcessingWindow(_client, filePath, trace);
                    win.Owner = this;
                    win.Show();
                    return;
                }
            }
        }

        _droppedFilePath         = filePath;
        DropSearchFileName.Text  = Path.GetFileName(filePath);
        DropSearchStatus.Text    = LanguageManager.Get("Drop.StatusMd5");
        DropInitial.Visibility   = Visibility.Collapsed;
        DropSearching.Visibility = Visibility.Visible;

        try
        {
            // Step 1 — exact MD5 match
            DropSearchStatus.Text = LanguageManager.Get("Drop.StatusHash");
            var md5   = ComputeMd5(filePath);
            var exact = await _client.Files.GetByHashAsync(md5);

            if (exact is not null)
            {
                ShowMatchDialog(filePath,
                [
                    new MatchRow(exact.FileId, exact.FileName, "100% (exact)")
                ], isExact: true);
                return;
            }

            // Step 2 — similarity across all files
            DropSearchStatus.Text = LanguageManager.Get("Drop.StatusContent");
            IReadOnlyList<SimilarFile> similar;
            try   { similar = await _client.Files.FindSimilarAsync(filePath); }
            catch { similar = []; }

            if (similar.Count > 0)
            {
                _currentSimilarFiles = similar;
                ShowMatchDialog(filePath,
                    similar.Select(m => new MatchRow(m.FileId, m.FileName, m.MatchPercentage)),
                    isExact: false);
                return;
            }

            // Step 3 — no matches → prompt user
            NoMatchFileName.Text = Path.GetFileName(filePath);
            DropSearching.Visibility    = Visibility.Collapsed;
            DropInitial.Visibility      = Visibility.Visible;
            PanelNoMatchDialog.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            var code = AppLogger.Error($"Drop search failed for '{Path.GetFileName(filePath)}'", ex);
            ResetDropZone();
            ShowToast($"Search failed ({code})", "✕");
        }
    }

    private bool _matchIsExact;
    private bool _filesLoaded;

    private void ShowMatchDialog(string filePath, IEnumerable<MatchRow> rows, bool isExact)
    {
        _matchIsExact = isExact;
        var rowList = rows.ToList();

        MatchFileName.Text = Path.GetFileName(filePath);
        MatchList.ItemsSource   = rowList;
        MatchList.SelectedIndex = 0;

        if (isExact)
        {
            MatchSubtitle.Text                  = LanguageManager.Get("Match.SubtitleExact");
            MatchWarningPanel.Visibility        = Visibility.Collapsed;
            MatchButtonsExact.Visibility        = Visibility.Visible;
            MatchButtonsPartial.Visibility      = Visibility.Collapsed;
        }
        else
        {
            MatchSubtitle.Text             = $"{rowList.Count} similar file(s) found. Choose how to proceed.";
            MatchWarningPanel.Visibility   = Visibility.Visible;
            MatchButtonsExact.Visibility   = Visibility.Collapsed;
            MatchButtonsPartial.Visibility = Visibility.Visible;
        }

        DropSearching.Visibility    = Visibility.Collapsed;
        DropInitial.Visibility      = Visibility.Visible;
        PanelMatchDialog.Visibility = Visibility.Visible;
    }

    private void ResetDropZone()
    {
        _droppedFilePath         = null;
        DropSearching.Visibility = Visibility.Collapsed;
        DropInitial.Visibility   = Visibility.Visible;
    }

    private void MatchClose_Click(object sender, RoutedEventArgs e)
    {
        PanelMatchDialog.Visibility = Visibility.Collapsed;
        ResetDropZone();
    }

    private void MatchOpen_Click(object sender, RoutedEventArgs e)
    {
        if (MatchList.SelectedItem is not MatchRow row) return;
        PanelMatchDialog.Visibility = Visibility.Collapsed;
        ResetDropZone();
        OpenFileDetail(row.FileId, row.FileName);
    }

    private void MatchAdd_Click(object sender, RoutedEventArgs e)
    {
        var path = _droppedFilePath;
        PanelMatchDialog.Visibility = Visibility.Collapsed;
        ResetDropZone();
        ShowPanel("Upload");
        if (path is not null) UploadFilePath.Text = path;
    }

    private void NoMatchAdd_Click(object sender, RoutedEventArgs e)
    {
        var path = _droppedFilePath;
        PanelNoMatchDialog.Visibility = Visibility.Collapsed;
        ResetDropZone();
        ShowPanel("Upload");
        if (path is not null) UploadFilePath.Text = path;
    }

    private void NoMatchCancel_Click(object sender, RoutedEventArgs e)
    {
        PanelNoMatchDialog.Visibility = Visibility.Collapsed;
        ResetDropZone();
    }

    private void MatchUse_Click(object sender, RoutedEventArgs e)
    {
        if (_droppedFilePath is null || _currentSimilarFiles.Count == 0) return;
        var filePath = _droppedFilePath;
        var best     = _currentSimilarFiles[0];
        PanelMatchDialog.Visibility = Visibility.Collapsed;
        ResetDropZone();
        var win = new FileDetailWindow(_client, best.FileId, best.FileName, sourceFilePath: filePath, currentUserId: _currentUserId);
        win.Owner = this;
        win.Closed += (_, _) => Activate();
        win.Show();
    }

    private void OpenFileDetail(Guid fileId, string fileName)
    {
        var win = new FileDetailWindow(_client, fileId, fileName, currentUserId: _currentUserId);
        win.Owner = this;
        win.Closed += (_, _) => Activate();
        win.Show();
    }

    private static string ComputeMd5(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
    }

    // ── Profile ───────────────────────────────────────────────────────────────

    private async Task LoadProfileAsync()
    {
        try
        {
            var p = await _client.Auth.GetProfileAsync();
            _email = p.Email;
            _currentUserId = p.Id;
            ProfileLabel.Text = p.Email;
            ProfEmail.Text    = p.Email;
            ProfTier.Text     = $"Plan: {p.Tier}";

            var usedMb  = p.StorageUsedBytes  / (1024.0 * 1024.0);
            var limitMb = p.StorageLimitBytes / (1024.0 * 1024.0);
            var pct     = limitMb > 0 ? Math.Min(100.0, usedMb / limitMb * 100.0) : 0;

            ProfStorageBar.Value = pct;
            ProfStorage.Text = limitMb >= 1024
                ? $"{usedMb / 1024:N2} GB of {limitMb / 1024:N0} GB used  ({pct:N1}%)"
                : $"{usedMb:N1} MB of {limitMb:N0} MB used  ({pct:N1}%)";

            _isBusiness = p.Tier.Equals("Business", StringComparison.OrdinalIgnoreCase);
            BulkLockOverlay.Visibility     = _isBusiness ? Visibility.Collapsed : Visibility.Visible;
            BulkImportTierBadge.Visibility = _isBusiness ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex) { AppLogger.Error("LoadProfile failed", ex); }
    }

    // ── Files ─────────────────────────────────────────────────────────────────

    private async Task LoadFilesAsync(bool silent = false)
    {
        if (!silent)
        {
            FilesStatus.Text      = "Loading…";
            FilesGrid.ItemsSource = null;
        }
        FilesLoadingBar.Visibility = Visibility.Visible;
        try
        {
            var page = await _client.Files.ListAsync();

            FilesGrid.ItemsSource = page.Items.Select(f => new FileRow(
                f.FileId,
                f.FileName,
                $"{f.VehicleMake} {f.VehicleModel} {f.VehicleVariant}",
                $"{f.ECUMake} {f.ECUModel}",
                $"{f.ReadHardware} / {f.ReadMode}",
                f.FileSize / 1024,
                f.PatchCount,
                f.UploadedAt.ToString("dd MMM yyyy")
            )).ToList();
            FilesStatus.Text = $"{page.Total} file(s) — double-click to open details";
            _filesLoaded = true;
        }
        catch (Exception ex)
        {
            var code = AppLogger.Error("LoadFiles failed", ex);
            FilesStatus.Text = $"Failed to load files ({code})";
            ShowToast($"Failed to load files ({code})", "✕");
        }
        finally
        {
            FilesLoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private void FilesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesGrid.SelectedItem is not FileRow row) return;
        OpenFileDetail(row.FileId, row.FileName);
    }

    private async void DeleteFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not Guid fileId) return;

        var row  = (FilesGrid.ItemsSource as IEnumerable<FileRow>)?.FirstOrDefault(r => r.FileId == fileId);
        var name = row?.FileName ?? fileId.ToString();

        var confirm = MessageBox.Show(
            $"Delete '{name}'?\n\nAll associated solutions will also be removed.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await _client.Files.DeleteAsync(fileId);
            _filesLoaded = false;
            await LoadFilesAsync();
            ShowToast($"'{name}' deleted.", "✓");
            AppLogger.Info($"Deleted file {fileId} ({name})");
        }
        catch (Exception ex)
        {
            var code = AppLogger.Error($"Delete file {fileId} failed", ex);
            ShowToast($"Delete failed ({code})", "✕");
        }
    }

    private async void DownloadFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not Guid fileId) return;

        var row  = (FilesGrid.ItemsSource as IEnumerable<FileRow>)?.FirstOrDefault(r => r.FileId == fileId);
        var name = row?.FileName ?? $"{fileId}.bin";

        var saveDlg = new SaveFileDialog
        {
            Title            = "Save original ECU file",
            Filter           = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
            FileName         = name,
        };
        if (saveDlg.ShowDialog() != true) return;

        ShowToast("Downloading…", "⬇");
        try
        {
            var data = await _client.Files.DownloadAsync(fileId);
            await File.WriteAllBytesAsync(saveDlg.FileName, data);
            AppLogger.Info($"Downloaded file {fileId} to {saveDlg.FileName}");
            var dlFolder = Path.GetDirectoryName(saveDlg.FileName);
            new SuccessDialog($"File downloaded!\n{Path.GetFileName(saveDlg.FileName)}", dlFolder) { Owner = this }.Show();
        }
        catch (Exception ex)
        {
            var code = AppLogger.Error($"Download file {fileId} failed", ex);
            ShowToast($"Download failed ({code})", "✕");
        }
    }

    private void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        var path = AppLogger.LogPath;
        try
        {
            if (!File.Exists(path))
            {
                ShowToast("No log file found yet.", "ℹ");
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            var code = AppLogger.Error("Failed to open log file", ex);
            ShowToast($"Could not open log ({code})", "✕");
        }
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select ECU file",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true)
            UploadFilePath.Text = dlg.FileName;
    }

    private async void UploadFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UploadFilePath.Text) || !File.Exists(UploadFilePath.Text))
        {
            UploadStatus.Text = "Please select a valid file.";
            ShowToast("Please select a valid file.", "⚠");
            return;
        }

        UploadStatus.Text         = "Uploading…";
        UploadProgress.Value      = 0;
        UploadProgress.Visibility = Visibility.Visible;

        try
        {
            var progress = new Progress<int>(pct =>
                Dispatcher.Invoke(() => UploadProgress.Value = pct));

            var meta = new FileMetadata
            {
                VehicleClass       = MdVehicleClass.Text.Trim(),
                VehicleMake        = MdVehicleMake.Text.Trim(),
                VehicleModel       = MdVehicleModel.Text.Trim(),
                VehicleVariant     = MdVehicleVariant.Text.Trim(),
                EngineType         = MdEngineType.Text.Trim(),
                ECUType            = MdECUType.Text.Trim(),
                ECUMake            = MdECUMake.Text.Trim(),
                ECUModel           = MdECUModel.Text.Trim(),
                ReadHardware       = MdReadHardware.Text.Trim(),
                ReadMode           = MdReadMode.Text.Trim(),
                ControllerHWNumber = NullIfEmpty(MdHWNumber.Text),
                ControllerSWNumber = NullIfEmpty(MdSWNumber.Text),
                PowerOutput        = int.TryParse(MdPower.Text,  out var pw) ? pw : null,
                TorqueOutput       = int.TryParse(MdTorque.Text, out var tq) ? tq : null,
            };

            var result = await _client.Files.UploadAsync(UploadFilePath.Text, meta, progress);
            UploadProgress.Value = 100;

            _filesLoaded = false;
            var displayName = meta.VehicleMake is { Length: > 0 } vm
                ? $"{vm} {meta.VehicleModel}".Trim()
                : Path.GetFileName(UploadFilePath.Text);

            if (result.IsDuplicate)
            {
                UploadStatus.Text = string.Empty;
                AppLogger.Info($"Duplicate upload: FileId={result.FileId}");
                ShowToast("Duplicate file — opening existing record.", "ℹ");
            }
            else
            {
                UploadStatus.Text = string.Empty;
                AppLogger.Info($"Upload complete: FileId={result.FileId}");
                var dlg = new SuccessDialog($"File uploaded successfully!\n{Path.GetFileName(UploadFilePath.Text)}") { Owner = this };
                dlg.Show();
            }
            OpenFileDetail(result.FileId, displayName);
        }
        catch (Exception ex)
        {
            var code = AppLogger.Error($"Upload failed for '{Path.GetFileName(UploadFilePath.Text)}'", ex);
            UploadStatus.Text = $"Upload failed ({code})";
            ShowToast($"Upload failed ({code})", "✕");
        }
        finally
        {
            UploadProgress.Visibility = Visibility.Collapsed;
        }
    }

    // ── Lookups / autocomplete ────────────────────────────────────────────────

    private async Task LoadLookupsAsync()
    {
        // Flat static lists (not cascade-dependent)
        (string Type, ComboBox Cb)[] flat =
        [
            ("VehicleClass", MdVehicleClass),
            ("EngineType",   MdEngineType),
            ("ECUType",      MdECUType),
            ("ReadHardware", MdReadHardware),
            ("ReadMode",     MdReadMode),
        ];

        foreach (var (type, cb) in flat)
        {
            try
            {
                var values = (await _client.Lookups.GetAsync(type)).ToList();
                AttachAutocomplete(cb, values);
            }
            catch (Exception ex) { AppLogger.Error($"Lookup load failed: {type}", ex); }
        }

        // Cascade: Vehicle
        MdVehicleClass.SelectionChanged += async (_, e) =>
        {
            if (e.AddedItems.Count == 0) return;
            var cls = MdVehicleClass.SelectedItem as string ?? MdVehicleClass.Text;
            await ReloadCascadeAsync(MdVehicleMake, "VehicleMake", vehicleClass: cls);
            ClearAndReset(MdVehicleModel, MdVehicleVariant);
        };

        MdVehicleMake.SelectionChanged += async (_, e) =>
        {
            if (e.AddedItems.Count == 0) return;
            var cls  = MdVehicleClass.SelectedItem as string ?? MdVehicleClass.Text;
            var make = MdVehicleMake.SelectedItem as string ?? MdVehicleMake.Text;
            await ReloadCascadeAsync(MdVehicleModel, "VehicleModel", vehicleClass: cls, vehicleMake: make);
            ClearAndReset(MdVehicleVariant);
        };

        MdVehicleModel.SelectionChanged += async (_, e) =>
        {
            if (e.AddedItems.Count == 0) return;
            var cls   = MdVehicleClass.SelectedItem as string ?? MdVehicleClass.Text;
            var make  = MdVehicleMake.SelectedItem as string  ?? MdVehicleMake.Text;
            var model = MdVehicleModel.SelectedItem as string ?? MdVehicleModel.Text;
            await ReloadCascadeAsync(MdVehicleVariant, "VehicleVariant",
                vehicleClass: cls, vehicleMake: make, vehicleModel: model);
        };

        // Cascade: ECU
        MdECUType.SelectionChanged += async (_, e) =>
        {
            if (e.AddedItems.Count == 0) return;
            var type = MdECUType.SelectedItem as string ?? MdECUType.Text;
            await ReloadCascadeAsync(MdECUMake, "ECUMake", ecuType: type);
            ClearAndReset(MdECUModel);
        };

        MdECUMake.SelectionChanged += async (_, e) =>
        {
            if (e.AddedItems.Count == 0) return;
            var type = MdECUType.SelectedItem as string ?? MdECUType.Text;
            var make = MdECUMake.SelectedItem as string ?? MdECUMake.Text;
            await ReloadCascadeAsync(MdECUModel, "ECUModel", ecuType: type, ecuMake: make);
        };

        // Seed initial cascaded lists with all values (no filter yet)
        await ReloadCascadeAsync(MdVehicleMake,   "VehicleMake");
        await ReloadCascadeAsync(MdVehicleModel,  "VehicleModel");
        await ReloadCascadeAsync(MdVehicleVariant,"VehicleVariant");
        await ReloadCascadeAsync(MdECUMake,       "ECUMake");
        await ReloadCascadeAsync(MdECUModel,      "ECUModel");
    }

    private async Task ReloadCascadeAsync(
        ComboBox cb, string field,
        string? vehicleClass = null, string? vehicleMake = null, string? vehicleModel = null,
        string? ecuType = null, string? ecuMake = null)
    {
        try
        {
            var values = (await _client.Lookups.GetCascadeAsync(
                field, vehicleClass, vehicleMake, vehicleModel, ecuType, ecuMake)).ToList();
            cb.Text = string.Empty;
            AttachAutocomplete(cb, values);
        }
        catch (Exception ex) { AppLogger.Error($"Cascade load failed: {field}", ex); }
    }

    private static void ClearAndReset(params ComboBox[] combos)
    {
        foreach (var cb in combos)
        {
            cb.Text       = string.Empty;
            cb.ItemsSource = Array.Empty<string>();
        }
    }

    private void AttachAutocomplete(ComboBox cb, List<string> all)
    {
        cb.ItemsSource = all;
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(cb.ItemsSource);

        var busy = false;

        cb.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            if (busy) return;
            var text = cb.Text;
            view.Filter = string.IsNullOrEmpty(text)
                ? null
                : o => ((string)o).Contains(text, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(text) && !view.IsEmpty && cb.IsKeyboardFocusWithin)
            {
                busy = true;
                var saved = text;
                Dispatcher.InvokeAsync(() =>
                {
                    if (cb.Text != saved) cb.Text = saved;
                    cb.IsDropDownOpen = true;
                    busy = false;
                }, DispatcherPriority.Input);
            }
        }));

        cb.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0)
            {
                busy = true;
                view.Filter = null;
                Dispatcher.InvokeAsync(() => busy = false, DispatcherPriority.Background);
            }
        };
    }

    // ── Bulk Import ───────────────────────────────────────────────────────────

    private void BulkSelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select folder containing ECU files" };
        if (dlg.ShowDialog() != true) return;

        var paths = Directory.GetFiles(dlg.FolderName, "*", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".bin" or ".ori" or ".hex" or ".bak" or "";
            })
            .ToList();

        if (paths.Count == 0)
        {
            ShowToast("No ECU files found in that folder.", "⚠");
            return;
        }

        _pendingBulkPaths = paths;
        BulkPathBox.Text  = $"{dlg.FolderName}  ({paths.Count} file{(paths.Count == 1 ? "" : "s")})";
        _bulkRows.Clear();
        BulkSummaryText.Text    = string.Empty;
        BulkOverallProgress.Visibility = Visibility.Collapsed;
        BulkParseBtn.IsEnabled  = true;
        BulkImportBtn.IsEnabled = false;
        BulkActivityLog.Items.Clear();
        WriteBulkLog("Ready — click Parse Filenames to continue.");
    }

    private void BulkSelectFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title     = "Select ECU files",
            Filter    = "ECU files (*.bin;*.ori;*.hex;*.bak)|*.bin;*.ori;*.hex;*.bak|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;

        _pendingBulkPaths = [.. dlg.FileNames];
        BulkPathBox.Text  = _pendingBulkPaths.Count == 1
            ? _pendingBulkPaths[0]
            : $"{_pendingBulkPaths.Count} files selected";
        _bulkRows.Clear();
        BulkSummaryText.Text    = string.Empty;
        BulkOverallProgress.Visibility = Visibility.Collapsed;
        BulkParseBtn.IsEnabled  = true;
        BulkImportBtn.IsEnabled = false;
        BulkActivityLog.Items.Clear();
        WriteBulkLog("Ready — click Parse Filenames to continue.");
    }

    private async void BulkParse_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingBulkPaths.Count == 0) return;

        BulkParseBtn.IsEnabled  = false;
        BulkImportBtn.IsEnabled = false;
        _bulkRows.Clear();
        BulkSummaryText.Text = "Parsing…";
        BulkActivityLog.Items.Clear();

        try
        {
            var filenames = _pendingBulkPaths.Select(p => Path.GetFileName(p) ?? p).ToList();
            WriteBulkLog($"Sending {filenames.Count} filename(s) to server for parsing…");

            var results = await _client.BulkImport.ParseFilenamesAsync(filenames);

            var pathByName = _pendingBulkPaths
                .GroupBy(Path.GetFileName)
                .ToDictionary(g => g.Key!, g => g.First());

            foreach (var dto in results)
            {
                var vehicle = string.Join(" ", new[]
                {
                    dto.VehicleClass, dto.VehicleMake, dto.VehicleModel
                }.Where(s => !string.IsNullOrWhiteSpace(s)));

                var ecu = string.Join(" ", new[]
                {
                    dto.ECUMake, dto.ECUModel
                }.Where(s => !string.IsNullOrWhiteSpace(s)));

                var row = new BulkRow
                {
                    FileName     = dto.OriginalName,
                    Vehicle      = vehicle,
                    Ecu          = ecu,
                    Power        = dto.PowerOutput.HasValue ? $"{dto.PowerOutput}" : null,
                    ErrorText    = dto.Errors.Length > 0 ? string.Join("; ", dto.Errors) : null,
                    ImportStatus = dto.Status == "Valid" ? "Valid" : "Invalid",
                    Parsed       = dto,
                };

                pathByName.TryGetValue(dto.OriginalName, out var filePath);
                row.FilePath = filePath;

                _bulkRows.Add(row);
            }

            var validCount   = _bulkRows.Count(r => r.ImportStatus == "Valid");
            var invalidCount = _bulkRows.Count(r => r.ImportStatus == "Invalid");
            BulkSummaryText.Text = $"{validCount} valid · {invalidCount} invalid";

            WriteBulkLog($"Parse complete: {validCount} valid, {invalidCount} invalid.");
            BulkImportBtn.IsEnabled = validCount > 0;
        }
        catch (Exception ex)
        {
            var code = AppLogger.Error("Bulk parse failed", ex);
            ShowToast($"Parse failed ({code})", "✕");
            BulkSummaryText.Text = $"Parse failed ({code})";
            WriteBulkLog($"Parse error — ref {code}");
        }
        finally
        {
            BulkParseBtn.IsEnabled = true;
        }
    }

    private async void BulkImportStart_Click(object sender, RoutedEventArgs e)
    {
        var validRows = _bulkRows.Where(r => r.ImportStatus == "Valid" && r.FilePath is not null).ToList();
        if (validRows.Count == 0) return;

        BulkImportBtn.IsEnabled = false;
        BulkParseBtn.IsEnabled  = false;
        BulkOverallProgress.Visibility = Visibility.Visible;
        BulkOverallProgress.Value      = 0;

        var done    = 0;
        var success = 0;
        var failed  = 0;

        WriteBulkLog($"Starting import of {validRows.Count} file(s)…");

        foreach (var row in validRows)
        {
            row.ImportStatus = "Uploading…";
            WriteBulkLog($"→ {row.FileName}");

            try
            {
                var result = await _client.BulkImport.ImportFileAsync(row.FilePath!, row.Parsed);

                if (result.Success)
                {
                    row.ImportStatus = "✓ Done";
                    success++;
                    AppLogger.Info($"Bulk import OK: {row.FileName} → FileId={result.FileId}");
                    WriteBulkLog($"  ✓ {row.FileName}");
                }
                else
                {
                    row.ImportStatus = "✗ Failed";
                    failed++;
                    var rejCode = AppLogger.Error($"Bulk import rejected: {row.FileName} — {result.Error}");
                    WriteBulkLog($"  ✗ {row.FileName} ({rejCode})");
                }
            }
            catch (Exception ex)
            {
                row.ImportStatus = "✗ Failed";
                failed++;
                var exCode = AppLogger.Error($"Bulk import exception: {row.FileName}", ex);
                WriteBulkLog($"  ✗ {row.FileName} ({exCode})");
            }

            done++;
            BulkOverallProgress.Value = (double)done / validRows.Count * 100;
        }

        BulkSummaryText.Text = $"{success} imported · {failed} failed";
        WriteBulkLog($"Import complete — {success} succeeded, {failed} failed.");
        ShowToast($"Bulk import: {success} uploaded, {failed} failed.", success > 0 ? "✓" : "⚠");

        _filesLoaded = false;
        BulkParseBtn.IsEnabled  = true;
        BulkImportBtn.IsEnabled = failed > 0;
    }

    private void WriteBulkLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        BulkActivityLog.Items.Add($"[{timestamp}]  {message}");
        BulkActivityLog.ScrollIntoView(BulkActivityLog.Items[^1]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
