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
                var resumeDlg = new ConfirmDialog(
                    "A previous session log was found for this file.",
                    "Would you like to continue from where you left off?",
                    confirmLabel: "Resume Session") { Owner = this };
                resumeDlg.ShowDialog();
                var answer = resumeDlg.Confirmed;

                if (answer)
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
                // Also run similarity search so USE can aggregate patches from all related files (≥78%)
                DropSearchStatus.Text = LanguageManager.Get("Drop.StatusContent");
                IReadOnlyList<SimilarFile> exactSimilar;
                try   { exactSimilar = await _client.Files.FindSimilarAsync(filePath); }
                catch { exactSimilar = []; }
                _currentSimilarFiles = exactSimilar;

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

            // Hide ADD when the best match is 100% — adding it would create a duplicate.
            var topPct = rowList.FirstOrDefault()?.SimilarityPct ?? string.Empty;
            MatchAddBtn.Visibility = topPct.StartsWith("100") ? Visibility.Collapsed : Visibility.Visible;
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
        OpenFileDetail(row.FileId, row.FileName, returnToDropZone: true);
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
        if (_droppedFilePath is null) return;
        var filePath = _droppedFilePath;
        PanelMatchDialog.Visibility = Visibility.Collapsed;
        ResetDropZone();

        FileDetailWindow win;
        if (_currentSimilarFiles.Count > 0)
        {
            // Aggregate patches from all similar files (≥78%), including exact matches
            win = new FileDetailWindow(_client, _currentSimilarFiles, sourceFilePath: filePath, currentUserId: _currentUserId);
        }
        else if (MatchList.SelectedItem is MatchRow row)
        {
            // Fallback: no similar files found — use only the matched file
            win = new FileDetailWindow(_client, row.FileId, row.FileName, sourceFilePath: filePath, currentUserId: _currentUserId);
        }
        else return;

        win.Owner = this;
        win.Closed += (_, _) => { Activate(); ShowPanel("DropZone"); };
        win.Show();
    }

    private void OpenFileDetail(Guid fileId, string fileName, bool returnToDropZone = false)
    {
        var win = new FileDetailWindow(_client, fileId, fileName, currentUserId: _currentUserId);
        win.Owner = this;
        win.Closed += (_, _) =>
        {
            Activate();
            if (returnToDropZone) ShowPanel("DropZone");
        };
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

            BulkLockOverlay.Visibility     = Visibility.Collapsed;
            BulkImportTierBadge.Visibility = Visibility.Collapsed;
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

        var dlg = new ConfirmDialog(
            $"Delete '{name}'?",
            "All associated solutions will also be removed. This cannot be undone.",
            isDanger: true, confirmLabel: "Delete") { Owner = this };
        dlg.ShowDialog();
        if (!dlg.Confirmed) return;

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

    private void ReportBug_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new BugReportDialog(_client) { Owner = this };
        dlg.ShowDialog();
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
            OpenFileDetail(result.FileId, displayName, returnToDropZone: true);
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

    // Track handlers per ComboBox so AttachAutocomplete can remove stale ones before re-adding.
    // Without this, every cascade reload stacks a new TextChanged handler on the same ComboBox,
    // causing competing filters and making it impossible to type after the first selection.
    private readonly Dictionary<ComboBox, TextChangedEventHandler>    _acTextHandlers = [];
    private readonly Dictionary<ComboBox, SelectionChangedEventHandler> _acSelHandlers = [];

    private async void RefreshLookups_Click(object sender, RoutedEventArgs e)
    {
        RefreshLookupsBtn.IsEnabled = false;
        RefreshLookupsBtn.Content   = "↻ Refreshing…";
        try   { await LoadLookupsAsync(); ShowToast("Lookup lists refreshed", "✓"); }
        catch { ShowToast("Failed to refresh lookups", "✕"); }
        finally
        {
            RefreshLookupsBtn.IsEnabled = true;
            RefreshLookupsBtn.Content   = "↻ Refresh lists";
        }
    }

    private bool _lookupEventsWired = false;

    private async Task LoadLookupsAsync()
    {
        // Flat static lists (not cascade-dependent)
        (string Type, ComboBox Cb)[] flat =
        [
            ("VehicleClass", MdVehicleClass),
            ("EngineType",   MdEngineType),
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

        // Wire cascade events only once — re-calling LoadLookupsAsync would stack duplicate handlers
        if (!_lookupEventsWired)
        {
            _lookupEventsWired = true;

        // Vehicle Class → clear dependent dropdowns (VehicleMake is from lookup table, not cascade)
        MdVehicleClass.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count == 0) return;
            ClearAndReset(MdVehicleModel);
        };

        // Vehicle Make → load models from lookup table filtered by class|make context
        MdVehicleMake.SelectionChanged += async (_, e) =>
        {
            if (e.AddedItems.Count == 0) return;
            var cls  = MdVehicleClass.SelectedItem as string ?? MdVehicleClass.Text;
            var make = MdVehicleMake.SelectedItem as string  ?? MdVehicleMake.Text;
            await ReloadLookupByContextAsync(MdVehicleModel, "VehicleModel", $"{cls}|{make}");
            ClearAndReset(MdVehicleVariant);
        };

        // Vehicle Model → load variants from lookup table filtered by class|make|model context
        MdVehicleModel.SelectionChanged += async (_, e) =>
        {
            if (e.AddedItems.Count == 0) return;
            var cls   = MdVehicleClass.SelectedItem as string ?? MdVehicleClass.Text;
            var make  = MdVehicleMake.SelectedItem as string  ?? MdVehicleMake.Text;
            var model = MdVehicleModel.SelectedItem as string ?? MdVehicleModel.Text;
            await ReloadLookupByContextAsync(MdVehicleVariant, "VehicleVariant", $"{cls}|{make}|{model}");
        };

        // ECU: Type → Make (filtered by type) → Model (filtered by type|make)
        MdECUType.SelectionChanged += async (_, e) =>
        {
            if (e.AddedItems.Count == 0) return;
            var type = MdECUType.SelectedItem as string ?? MdECUType.Text;
            await ReloadLookupByContextAsync(MdECUMake, "ECUMake", type);
            ClearAndReset(MdECUModel);
        };

        MdECUMake.SelectionChanged += async (_, e) =>
        {
            if (e.AddedItems.Count == 0) return;
            var type = MdECUType.SelectedItem as string ?? MdECUType.Text;
            var make = MdECUMake.SelectedItem as string ?? MdECUMake.Text;
            await ReloadLookupByContextAsync(MdECUModel, "ECUModel", $"{type}|{make}");
        };

        } // end _lookupEventsWired

        // VehicleMake comes from the seeded lookup table (all makes, not just uploaded files)
        try
        {
            var makes = (await _client.Lookups.GetAsync("VehicleMake")).ToList();
            AttachAutocomplete(MdVehicleMake, makes);
        }
        catch (Exception ex) { AppLogger.Error("Lookup load failed: VehicleMake", ex); }

        // VehicleModel is empty until the user picks a make (too many to show unfiltered)
        ClearAndReset(MdVehicleModel);

        // ECUType from lookup table; ECUMake and ECUModel are empty until Type is selected
        try
        {
            var ecuTypes = (await _client.Lookups.GetAsync("ECUType")).ToList();
            AttachAutocomplete(MdECUType, ecuTypes);
        }
        catch (Exception ex) { AppLogger.Error("Lookup load failed: ECUType", ex); }
        ClearAndReset(MdECUMake, MdECUModel);
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

    private async Task ReloadLookupByContextAsync(ComboBox cb, string type, string context)
    {
        try
        {
            var values = (await _client.Lookups.GetAsync(type, context)).ToList();
            cb.Text = string.Empty;
            AttachAutocomplete(cb, values);
        }
        catch (Exception ex) { AppLogger.Error($"Lookup load failed: {type} context={context}", ex); }
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
        // Remove previously attached handlers so re-calling this (on cascade reload) doesn't stack them.
        if (_acTextHandlers.TryGetValue(cb, out var oldText)) cb.RemoveHandler(TextBox.TextChangedEvent, oldText);
        if (_acSelHandlers.TryGetValue(cb, out var oldSel))   cb.SelectionChanged -= oldSel;

        cb.ItemsSource = all;
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(cb.ItemsSource);

        var busy = false;

        TextChangedEventHandler textHandler = (_, _) =>
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
        };

        SelectionChangedEventHandler selHandler = (_, e) =>
        {
            if (e.AddedItems.Count > 0)
            {
                busy = true;
                view.Filter = null;
                Dispatcher.InvokeAsync(() => busy = false, DispatcherPriority.Background);
            }
        };

        cb.AddHandler(TextBox.TextChangedEvent, textHandler);
        cb.SelectionChanged += selHandler;

        _acTextHandlers[cb] = textHandler;
        _acSelHandlers[cb]  = selHandler;
    }

    // ── Bulk Import ───────────────────────────────────────────────────────────

    private const string WinOlsFormatString =
        "%Vehicle.Type%_%ECU.Use%_%Vehicle.Producer%_%Vehicle.Series%-%Vehicle.Model%_%Vehicle.Modelyear%_%Vehicle.Build%_%Engine.OutputPS%_%ECU.Producer%_%ECU.Build%_%File.ReadHardware%_%More.Versionname%_%Engine.OutputKW%_%Engine.MaxTorque%_%Engine.Type%.bin";

    private void BulkCopyFormat_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(WinOlsFormatString);
        BulkCopyFormatBtn.Content = "Copied!";
        var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { BulkCopyFormatBtn.Content = "Copy"; timer.Stop(); };
        timer.Start();
    }

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
