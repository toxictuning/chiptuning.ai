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

public partial class MainWindow : Window
{
    private readonly ChiptuningAiClient _client;
    private string? _droppedFilePath;

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
        if (_client.TokenExpiresAt is not { } expiresAt) return;

        var delay = expiresAt - DateTimeOffset.UtcNow - TimeSpan.FromSeconds(60);
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

        _refreshTimer = new DispatcherTimer { Interval = delay };
        _refreshTimer.Tick += async (_, _) =>
        {
            _refreshTimer.Stop();
            try
            {
                await _client.Auth.RefreshAsync();
                StartRefreshTimer();
            }
            catch
            {
                ShowToast("Session expired — please sign in again.", "⚠");
            }
        };
        _refreshTimer.Start();
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainWindow(ChiptuningAiClient client)
    {
        InitializeComponent();
        _client = client;
        ToastList.ItemsSource = _toasts;

        ThemeToggleBtn.Content = ThemeManager.IsDark ? "☾" : "☀";
        UpdateSwatchHighlight();

        Loaded += async (_, _) =>
        {
            await LoadProfileAsync();
            _ = LoadLookupsAsync();
            StartRefreshTimer();
        };

        ShowPanel("DropZone");
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
        PanelPatches.Visibility  = name == "Patches"  ? Visibility.Visible : Visibility.Collapsed;
        PanelProfile.Visibility  = name == "Profile"  ? Visibility.Visible : Visibility.Collapsed;

        if (name == "Files")   _ = LoadFilesAsync();
        if (name == "Patches") _ = LoadPatchesAsync();
        if (name == "Profile") _ = LoadProfileAsync();
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
        _droppedFilePath         = filePath;
        DropSearchFileName.Text  = Path.GetFileName(filePath);
        DropSearchStatus.Text    = "Computing MD5…";
        DropInitial.Visibility   = Visibility.Collapsed;
        DropSearching.Visibility = Visibility.Visible;

        try
        {
            // Step 1 — exact MD5 match
            DropSearchStatus.Text = "Searching by hash…";
            var md5   = ComputeMd5(filePath);
            var exact = await _client.Files.GetByHashAsync(md5);

            if (exact is not null)
            {
                ShowMatchDialog(filePath,
                [
                    new MatchRow(exact.FileId, exact.FileName, "100% (exact)")
                ]);
                return;
            }

            // Step 2 — chunk similarity across all files
            DropSearchStatus.Text = "Searching by content…";
            IReadOnlyList<SimilarFile> similar;
            try   { similar = await _client.Files.FindSimilarAsync(filePath); }
            catch { similar = []; }

            if (similar.Count > 0)
            {
                ShowMatchDialog(filePath,
                    similar.Select(m => new MatchRow(m.FileId, m.FileName, m.MatchPercentage)));
                return;
            }

            // Step 3 — no matches → upload
            UploadFilePath.Text = filePath;
            ResetDropZone();
            ShowPanel("Upload");
            ShowToast("No matches found — file ready to upload.", "ℹ");
        }
        catch (Exception ex)
        {
            ResetDropZone();
            ShowToast($"Search failed: {ex.Message}", "✕");
        }
    }

    private void ShowMatchDialog(string filePath, IEnumerable<MatchRow> rows)
    {
        MatchFileName.Text          = Path.GetFileName(filePath);
        MatchList.ItemsSource       = rows.ToList();
        MatchList.SelectedIndex     = 0;
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

    private void OpenFileDetail(Guid fileId, string fileName)
    {
        var win = new FileDetailWindow(_client, fileId, fileName);
        win.Owner = this;
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
            ProfileLabel.Text = p.Email;
            ProfEmail.Text    = $"Email:    {p.Email}";
            ProfTier.Text     = $"Plan:     {p.Tier}";
            ProfStorage.Text  = $"Storage:  {p.StorageUsedBytes / 1024:N0} KB used";
        }
        catch { }
    }

    // ── Files ─────────────────────────────────────────────────────────────────

    private async Task LoadFilesAsync()
    {
        FilesStatus.Text      = "Loading…";
        FilesGrid.ItemsSource = null;
        try
        {
            var page = await _client.Files.ListAsync();

            FilesGrid.ItemsSource = page.Items.Select(f => new
            {
                f.FileId,
                f.FileName,
                Vehicle  = $"{f.VehicleMake} {f.VehicleModel} {f.VehicleVariant}",
                Ecu      = $"{f.ECUMake} {f.ECUModel}",
                Read     = $"{f.ReadHardware} / {f.ReadMode}",
                SizeKb   = f.FileSize / 1024,
                f.PatchCount,
                Uploaded = f.UploadedAt.ToString("dd MMM yyyy"),
            }).ToList();
            FilesStatus.Text = $"{page.Total} file(s) — double-click to open details";
        }
        catch (Exception ex)
        {
            FilesStatus.Text = $"Error: {ex.Message}";
            ShowToast($"Failed to load files: {ex.Message}", "✕");
        }
    }

    private void FilesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesGrid.SelectedItem is null) return;

        var item = FilesGrid.SelectedItem;
        var fileIdProp = item.GetType().GetProperty("FileId");
        var fileNameProp = item.GetType().GetProperty("FileName");
        if (fileIdProp?.GetValue(item) is not Guid fileId) return;
        var fileName = fileNameProp?.GetValue(item) as string ?? "File";

        OpenFileDetail(fileId, fileName);
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

            if (result.IsDuplicate)
            {
                UploadStatus.Text = $"Duplicate detected — existing FileId: {result.FileId}";
                ShowToast("Duplicate file detected.", "ℹ");
            }
            else
            {
                UploadStatus.Text = $"Upload complete! FileId: {result.FileId}";
                ShowToast("File uploaded successfully.", "✓");
            }
        }
        catch (Exception ex)
        {
            UploadStatus.Text = $"Error: {ex.Message}";
            ShowToast($"Upload failed: {ex.Message}", "✕");
        }
        finally
        {
            UploadProgress.Visibility = Visibility.Collapsed;
        }
    }

    // ── Patches ───────────────────────────────────────────────────────────────

    private async Task LoadPatchesAsync()
    {
        PatchesStatus.Text      = "Select a file in the Files tab to see its patches.";
        PatchesGrid.ItemsSource = null;
        await Task.CompletedTask;
    }

    // ── Lookups / autocomplete ────────────────────────────────────────────────

    private async Task LoadLookupsAsync()
    {
        (string Type, ComboBox Cb)[] targets =
        [
            ("VehicleClass", MdVehicleClass),
            ("VehicleMake",  MdVehicleMake),
            ("VehicleModel", MdVehicleModel),
            ("EngineType",   MdEngineType),
            ("ECUType",      MdECUType),
            ("ECUMake",      MdECUMake),
            ("ECUModel",     MdECUModel),
            ("ReadHardware", MdReadHardware),
            ("ReadMode",     MdReadMode),
        ];

        foreach (var (type, cb) in targets)
        {
            try
            {
                var values = (await _client.Lookups.GetAsync(type)).ToList();
                AttachAutocomplete(cb, values);
            }
            catch { }
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
