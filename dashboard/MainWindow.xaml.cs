using System.Collections.ObjectModel;
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
using ChiptuningAi.Client.Files;
using ChiptuningAi.Dashboard.Services;

namespace ChiptuningAi.Dashboard;

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
    private string? _droppedFilePath;
    private IReadOnlyList<SimilarFile> _currentSimilarFiles = [];

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
                AppLogger.Error("Token refresh failed", ex);
                SessionStore.Clear();
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
        ToastList.ItemsSource = _toasts;

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
        PanelDropZone.Visibility = name == "DropZone" ? Visibility.Visible : Visibility.Collapsed;
        PanelFiles.Visibility    = name == "Files"    ? Visibility.Visible : Visibility.Collapsed;
        PanelUpload.Visibility   = name == "Upload"   ? Visibility.Visible : Visibility.Collapsed;
        PanelProfile.Visibility  = name == "Profile"  ? Visibility.Visible : Visibility.Collapsed;

        if (name == "Upload")  ResetUploadForm();
        if (name == "Files" && !_filesLoaded) _ = LoadFilesAsync();
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
            AppLogger.Error($"Drop search failed for '{Path.GetFileName(filePath)}'", ex);
            ResetDropZone();
            ShowToast($"Search failed: {ex.Message}", "✕");
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
        var win = new FileDetailWindow(_client, best.FileId, best.FileName, sourceFilePath: filePath);
        win.Owner = this;
        win.Closed += (_, _) => Activate();
        win.Show();
    }

    private void OpenFileDetail(Guid fileId, string fileName)
    {
        var win = new FileDetailWindow(_client, fileId, fileName);
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
        }
        catch (Exception ex) { AppLogger.Error("LoadProfile failed", ex); }
    }

    // ── Files ─────────────────────────────────────────────────────────────────

    private async Task LoadFilesAsync()
    {
        FilesStatus.Text          = "Loading…";
        FilesGrid.ItemsSource     = null;
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
            AppLogger.Error("LoadFiles failed", ex);
            FilesStatus.Text = $"Error: {ex.Message}";
            ShowToast($"Failed to load files: {ex.Message}", "✕");
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
            $"Delete '{name}'?\n\nAll associated patches will also be removed.",
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
            AppLogger.Error($"Delete file {fileId} failed", ex);
            ShowToast($"Delete failed: {ex.Message}", "✕");
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
            ShowToast($"Saved — {Path.GetFileName(saveDlg.FileName)}", "✓");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Download file {fileId} failed", ex);
            ShowToast($"Download failed: {ex.Message}", "✕");
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
            AppLogger.Error("Failed to open log file", ex);
            ShowToast($"Could not open log: {path}", "✕");
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
            AppLogger.Error($"Upload failed for '{Path.GetFileName(UploadFilePath.Text)}'", ex);
            UploadStatus.Text = "Upload failed. Check the log for details.";
            ShowToast("Upload failed.", "✕");
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
