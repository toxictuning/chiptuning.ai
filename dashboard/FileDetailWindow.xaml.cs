using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChiptuningAi.Client;
using ChiptuningAi.Client.Files;
using ChiptuningAi.Dashboard.Services;
using Microsoft.Win32;

namespace ChiptuningAi.Dashboard;

file sealed class PatchRow : INotifyPropertyChanged
{
    public Guid   PatchId     { get; init; }
    public string Description { get; init; } = "";
    public string Version     { get; init; } = "";
    public string SizeKb      { get; init; } = "";
    public string Created     { get; init; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class FileDetailWindow : Window
{
    private readonly ChiptuningAiClient _client;
    private readonly Guid _fileId;
    private readonly string? _prefilledSourcePath;

    public FileDetailWindow(ChiptuningAiClient client, Guid fileId, string? displayName = null, string? sourceFilePath = null)
    {
        InitializeComponent();
        _client = client;
        _fileId = fileId;
        _prefilledSourcePath = sourceFilePath;
        TitleText.Text = displayName ?? "File Detail";
        Loaded += async (_, _) =>
        {
            if (_prefilledSourcePath is not null)
                GenerateSourcePath.Text = _prefilledSourcePath;
            await LoadAsync();
        };
    }

    // ── Title bar ─────────────────────────────────────────────────────────────

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (e.LeftButton == MouseButtonState.Pressed && !_isManuallyMaximized)
            DragMove();
    }

    private bool _isManuallyMaximized;
    private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;

    private void ToggleMaximize()
    {
        if (_isManuallyMaximized)
        {
            Left = _restoreLeft; Top = _restoreTop;
            Width = _restoreWidth; Height = _restoreHeight;
            _isManuallyMaximized = false;
            MaxBtn.Content = "☐";
        }
        else
        {
            _restoreLeft = Left; _restoreTop = Top;
            _restoreWidth = Width; _restoreHeight = Height;
            var wa = SystemParameters.WorkArea;
            Left = wa.Left; Top = wa.Top;
            Width = wa.Width; Height = wa.Height;
            _isManuallyMaximized = true;
            MaxBtn.Content = "❐";
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal && _isManuallyMaximized)
        {
            _isManuallyMaximized = false;
            MaxBtn.Content = "☐";
        }
    }


    // ── Data loading ──────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        try
        {
            var file = await _client.Files.GetAsync(_fileId);
            if (file is null)
            {
                SidebarLoading.Text = "File not found.";
                return;
            }
            PopulateSidebar(file);
            await LoadPatchesAsync(file);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"FileDetail load failed for {_fileId}", ex);
            SidebarLoading.Text = $"Error: {ex.Message}";
        }
    }

    private void PopulateSidebar(FileDetails f)
    {
        _loadedFile = f;
        TitleText.Text = f.FileName;

        InfoFileName.Text   = f.FileName;
        InfoFileSize.Text   = $"{f.FileSize / 1024.0:N1} KB  ({f.FileSize:N0} bytes)";
        InfoMD5.Text        = f.MD5;
        InfoHash.Text       = f.Hash;
        InfoVehicle.Text    = $"{f.VehicleClass}\n{f.VehicleMake} {f.VehicleModel} {f.VehicleVariant}\n{f.EngineType}";
        InfoEcu.Text        = $"{f.ECUType}  {f.ECUMake} {f.ECUModel}";
        InfoRead.Text       = $"Read: {f.ReadHardware} / {f.ReadMode}"
            + (f.ControllerHWNumber is not null ? $"\nHW: {f.ControllerHWNumber}" : "")
            + (f.ControllerSWNumber is not null ? $"  SW: {f.ControllerSWNumber}" : "");
        InfoUploadedAt.Text = f.UploadedAt.ToString("dd MMM yyyy HH:mm UTC");
        InfoUploadedBy.Text = f.UploadedByEmail is not null
            ? $"By: {f.UploadedByEmail}"
            : $"User ID: {f.UploadedBy}";

        SidebarLoading.Visibility  = Visibility.Collapsed;
        SidebarContent.Visibility  = Visibility.Visible;
    }

    private async Task LoadPatchesAsync(FileDetails f)
    {
        PatchesStatus.Text = "Loading patches…";
        PatchesGrid.ItemsSource = null;
        try
        {
            var page = await _client.Patches.ListAsync(f.FileId, take: 100);
            if (page.Items.Count == 0)
            {
                PatchesStatus.Text = "No patches found for this file.";
                return;
            }
            PatchesGrid.ItemsSource = page.Items.Select(p => new PatchRow
            {
                PatchId     = p.PatchId,
                Description = p.Description ?? "(no description)",
                Version     = p.Version ?? "—",
                SizeKb      = $"{p.FileSize / 1024.0:N1}",
                Created     = p.CreatedAt.ToString("dd MMM yyyy"),
            }).ToList();
            PatchesStatus.Text = $"{page.Total} patch(es)";
        }
        catch (Exception ex)
        {
            PatchesStatus.Text = $"Error: {ex.Message}";
        }
    }

    // ── Patch actions: Delete / Rename / Replace ──────────────────────────────

    private async void DeletePatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Guid patchId) return;

        var confirm = MessageBox.Show(
            "Permanently delete this patch?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await _client.Patches.DeleteAsync(patchId);
            if (_loadedFile is not null) await LoadPatchesAsync(_loadedFile);
            PatchesStatus.Text = "Patch deleted.";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Delete patch {patchId} failed", ex);
            PatchesStatus.Text = "Delete failed — check the log.";
        }
    }

    private async void RenamePatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Guid patchId) return;

        var dlg = new RenameDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await _client.Patches.UpdateAsync(patchId, dlg.NewDescription, dlg.NewVersion);
            if (_loadedFile is not null) await LoadPatchesAsync(_loadedFile);
            PatchesStatus.Text = "Patch renamed.";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Rename patch {patchId} failed", ex);
            PatchesStatus.Text = "Rename failed — check the log.";
        }
    }

    private async void ReplacePatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Guid patchId) return;
        if (_loadedFile is null) return;

        var dlg = new OpenFileDialog
        {
            Title  = "Select replacement ECU file",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        PatchesStatus.Text = "Uploading replacement…";
        try
        {
            // Upload new binary against same parent, then remove the old patch
            await _client.Patches.UploadAsync(dlg.FileName, _loadedFile.FileId);
            await _client.Patches.DeleteAsync(patchId);
            await LoadPatchesAsync(_loadedFile);
            PatchesStatus.Text = "Patch replaced.";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Replace patch {patchId} failed", ex);
            PatchesStatus.Text = "Replace failed — check the log.";
        }
    }

    // ── Generate ──────────────────────────────────────────────────────────────

    private void GenerateSource_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void GenerateSource_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0) GenerateSourcePath.Text = files[0];
    }

    private void BrowseGenerateSource_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select source ECU file",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true)
            GenerateSourcePath.Text = dlg.FileName;
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        var sourcePath = GenerateSourcePath.Text.Trim();
        var placeholder = "Drop source ECU file here or Browse…";
        bool usingOriginal = false;
        string? tempFile = null;

        if (string.IsNullOrEmpty(sourcePath) || sourcePath == placeholder || !File.Exists(sourcePath))
        {
            if (_loadedFile is null)
            {
                GenerateStatus.Text = "Please select the source ECU file first.";
                return;
            }
            usingOriginal = true;
        }

        var selectedRows = (PatchesGrid.ItemsSource as IEnumerable<PatchRow>)?
            .Where(r => r.IsSelected).ToList() ?? [];

        var selected = selectedRows.Select(r => r.PatchId).ToList();

        if (selected.Count == 0)
        {
            GenerateStatus.Text = "Tick at least one patch before generating.";
            return;
        }

        var prefix = _loadedFile is not null ? BuildFilePrefix(_loadedFile) : null;
        var suggestedName = BuildOutputFileName(selectedRows.Select(r => r.Description), sourcePath, usingOriginal, prefix);

        var saveDlg = new SaveFileDialog
        {
            Title            = "Save generated file",
            Filter           = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
            FileName         = suggestedName,
            InitialDirectory = usingOriginal ? "" : System.IO.Path.GetDirectoryName(sourcePath) ?? "",
        };
        if (saveDlg.ShowDialog() != true) return;

        GenerateStatus.Text = usingOriginal
            ? $"Downloading original file, then applying {selected.Count} patch(es)…"
            : $"Applying {selected.Count} patch(es)…";
        try
        {
            if (usingOriginal)
            {
                var bytes = await _client.Files.DownloadAsync(_loadedFile!.FileId);
                tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    $"cta_{_loadedFile.FileId:N}.bin");
                await File.WriteAllBytesAsync(tempFile, bytes);
                sourcePath = tempFile;
                GenerateStatus.Text = $"Applying {selected.Count} patch(es) to original…";
            }

            var result = await _client.Patches.ProcessAsync(sourcePath, selected);
            await File.WriteAllBytesAsync(saveDlg.FileName, result);
            GenerateStatus.Text = $"Done — saved to {System.IO.Path.GetFileName(saveDlg.FileName)}";
            AppLogger.Info($"Generated {saveDlg.FileName} using {selected.Count} patch(es)");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Generate failed", ex);
            GenerateStatus.Text = "Generate failed — check the log.";
        }
        finally
        {
            if (tempFile is not null && File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private static string BuildFilePrefix(ChiptuningAi.Client.Files.FileDetails f)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        static string Clean(string s)
        {
            s = new string(s.Select(c => " /\\:*?\"<>|".Contains(c) ? '_' : c).ToArray()).Trim();
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "_");
        }
        var parts = new[] { f.VehicleMake, f.VehicleModel, f.VehicleVariant, f.ECUMake, f.ECUModel, f.ReadHardware, f.ReadMode }
            .Select(Clean).Where(s => s.Length > 0);
        return string.Join("_", parts);
    }

    private static string BuildOutputFileName(
        IEnumerable<string> descriptions, string sourcePath, bool usingOriginal, string? prefix = null)
    {
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        var descList = descriptions.ToList();
        string descPart = string.Empty;

        if (descList.Count > 0)
        {
            var parts = descList.Select(description =>
            {
                var desc = System.Text.RegularExpressions.Regex.Replace(
                    description, @"\b(ori(ginal)?)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                desc = new string(desc.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
                desc = System.Text.RegularExpressions.Regex.Replace(desc.Trim(), @"\s+", "_");
                return desc;
            }).Where(s => s.Length > 0);
            descPart = string.Join("_", parts);
        }

        string baseName;
        if (prefix is { Length: > 0 } p)
            baseName = descPart.Length > 0 ? $"{p}_{descPart}" : p;
        else
            baseName = descPart.Length > 0 ? descPart : System.IO.Path.GetFileNameWithoutExtension(sourcePath) + "_generated";

        return baseName + ".bin";
    }

    // ── Patch upload ──────────────────────────────────────────────────────────

    private FileDetails? _loadedFile;

    private void PatchZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void PatchZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0) PatchFilePath.Text = files[0];
    }

    private void BrowsePatch_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select modified ECU file",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true)
            PatchFilePath.Text = dlg.FileName;
    }

    private async void UploadPatch_Click(object sender, RoutedEventArgs e)
    {
        var path = PatchFilePath.Text.Trim();
        if (string.IsNullOrEmpty(path) || path == "No file selected" || !File.Exists(path))
        {
            PatchUploadStatus.Text = "Please select the modified ECU file first.";
            return;
        }

        PatchUploadStatus.Text = "Uploading patch…";
        try
        {
            var desc    = NullIfEmpty(PatchDescription.Text == "Description" ? null : PatchDescription.Text);
            var version = NullIfEmpty(PatchVersion.Text == "v1.0" ? "v1.0" : PatchVersion.Text);

            var result = await _client.Patches.UploadAsync(path, _fileId, desc, version);
            AppLogger.Info($"Patch uploaded: {result.PatchId} for file {_fileId}");
            PatchUploadStatus.Text = string.Empty;
            PatchFilePath.Text     = "No file selected";

            var successDlg = new SuccessDialog($"Patch uploaded successfully!\n{desc ?? System.IO.Path.GetFileName(path)}") { Owner = this };
            successDlg.Show();

            if (_loadedFile is not null) await LoadPatchesAsync(_loadedFile);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Patch upload failed for file {_fileId}", ex);
            PatchUploadStatus.Text = "Patch upload failed. Check the log for details.";
        }
    }

    // ── Download original ─────────────────────────────────────────────────────

    private async void DownloadOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedFile is null) return;

        var saveDlg = new SaveFileDialog
        {
            Title            = "Save original ECU file",
            Filter           = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
            FileName         = _loadedFile.FileName,
        };
        if (saveDlg.ShowDialog() != true) return;

        PatchesStatus.Text = "Downloading original…";
        try
        {
            var data = await _client.Files.DownloadAsync(_loadedFile.FileId);
            await File.WriteAllBytesAsync(saveDlg.FileName, data);
            AppLogger.Info($"Downloaded original file {_fileId} to {saveDlg.FileName}");
            PatchesStatus.Text = $"Saved — {System.IO.Path.GetFileName(saveDlg.FileName)}";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Download original failed for {_fileId}", ex);
            PatchesStatus.Text = $"Download failed: {ex.Message}";
        }
    }

    // ── Metadata edit ─────────────────────────────────────────────────────────

    private void EditMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedFile is null) return;

        EditVehicleClass.Text   = _loadedFile.VehicleClass;
        EditVehicleMake.Text    = _loadedFile.VehicleMake;
        EditVehicleModel.Text   = _loadedFile.VehicleModel;
        EditVehicleVariant.Text = _loadedFile.VehicleVariant;
        EditEngineType.Text     = _loadedFile.EngineType;
        EditECUType.Text        = _loadedFile.ECUType;
        EditECUMake.Text        = _loadedFile.ECUMake;
        EditECUModel.Text       = _loadedFile.ECUModel;
        EditReadHardware.Text   = _loadedFile.ReadHardware;
        EditReadMode.Text       = _loadedFile.ReadMode;
        EditHWNumber.Text       = _loadedFile.ControllerHWNumber ?? string.Empty;
        EditSWNumber.Text       = _loadedFile.ControllerSWNumber ?? string.Empty;
        EditStatus.Text         = string.Empty;

        SidebarContent.Visibility = Visibility.Collapsed;
        SidebarEdit.Visibility    = Visibility.Visible;
    }

    private void EditCancel_Click(object sender, RoutedEventArgs e)
    {
        SidebarEdit.Visibility    = Visibility.Collapsed;
        SidebarContent.Visibility = Visibility.Visible;
    }

    private async void EditSave_Click(object sender, RoutedEventArgs e)
    {
        EditStatus.Text = "Saving…";
        try
        {
            var req = new ChiptuningAi.Client.Files.UpdateFileRequest
            {
                VehicleClass      = EditVehicleClass.Text.Trim(),
                VehicleMake       = EditVehicleMake.Text.Trim(),
                VehicleModel      = EditVehicleModel.Text.Trim(),
                VehicleVariant    = EditVehicleVariant.Text.Trim(),
                EngineType        = EditEngineType.Text.Trim(),
                ECUType           = EditECUType.Text.Trim(),
                ECUMake           = EditECUMake.Text.Trim(),
                ECUModel          = EditECUModel.Text.Trim(),
                ReadHardware      = EditReadHardware.Text.Trim(),
                ReadMode          = EditReadMode.Text.Trim(),
                ControllerHWNumber = NullIfEmpty(EditHWNumber.Text),
                ControllerSWNumber = NullIfEmpty(EditSWNumber.Text),
            };

            await _client.Files.UpdateAsync(_fileId, req);
            AppLogger.Info($"Metadata updated for file {_fileId}");

            // Reload sidebar with fresh data
            var updated = await _client.Files.GetAsync(_fileId);
            PopulateSidebar(updated);
            SidebarEdit.Visibility    = Visibility.Collapsed;
            SidebarContent.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Metadata update failed for {_fileId}", ex);
            EditStatus.Text = $"Save failed: {ex.Message}";
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

