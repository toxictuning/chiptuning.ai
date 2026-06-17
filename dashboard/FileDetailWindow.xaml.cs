using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChiptuningAi.Client;
using ChiptuningAi.Client.Files;

namespace ChiptuningAi.Dashboard;

public partial class FileDetailWindow : Window
{
    private readonly ChiptuningAiClient _client;
    private readonly Guid _fileId;

    public FileDetailWindow(ChiptuningAiClient client, Guid fileId, string? displayName = null)
    {
        InitializeComponent();
        _client = client;
        _fileId = fileId;
        TitleText.Text = displayName ?? "File Detail";

        HighlightTab(TabPatches);
        Loaded += async (_, _) => await LoadAsync();
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
            Dispatcher.BeginInvoke(() =>
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Left; Top = wa.Top;
                Width = wa.Width; Height = wa.Height;
                WindowState = WindowState.Normal;
            });
        }
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void TabPatches_Click(object sender, RoutedEventArgs e)
    {
        PanelPatches.Visibility = Visibility.Visible;
        PanelChunks.Visibility  = Visibility.Collapsed;
        HighlightTab(TabPatches);
    }

    private void TabChunks_Click(object sender, RoutedEventArgs e)
    {
        PanelPatches.Visibility = Visibility.Collapsed;
        PanelChunks.Visibility  = Visibility.Visible;
        HighlightTab(TabChunks);
    }

    private void HighlightTab(System.Windows.Controls.Button active)
    {
        var accent = (Brush)Application.Current.Resources["AccentButtonBg"];
        var none   = Brushes.Transparent;
        TabPatches.BorderBrush = active == TabPatches ? accent : none;
        TabChunks.BorderBrush  = active == TabChunks  ? accent : none;
        TabPatches.BorderThickness = new Thickness(0, 0, 0, active == TabPatches ? 2 : 0);
        TabChunks.BorderThickness  = new Thickness(0, 0, 0, active == TabChunks  ? 2 : 0);
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        FileDetails? file = null;
        try
        {
            file = await _client.Files.GetAsync(_fileId);
        }
        catch (Exception ex)
        {
            SidebarLoading.Text = $"Error loading file: {ex.Message}";
            return;
        }

        PopulateSidebar(file);
        await LoadPatchesAsync(file);
        PopulateChunks(file);
    }

    private void PopulateSidebar(FileDetails f)
    {
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
            PatchesGrid.ItemsSource = page.Items.Select(p => new
            {
                Description = p.Description ?? "(no description)",
                Version     = p.Version ?? "—",
                SizeKb      = $"{p.FileSize / 1024.0:N1}",
                p.IsActive,
                Created     = p.CreatedAt.ToString("dd MMM yyyy"),
            }).ToList();
            PatchesStatus.Text = $"{page.Total} patch(es)";
        }
        catch (Exception ex)
        {
            PatchesStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void PopulateChunks(FileDetails f)
    {
        if (f.ChunkHashes is { Count: > 0 })
        {
            ChunksHeader.Text      = $"{f.ChunkHashes.Count} DNA blocks";
            ChunksList.ItemsSource = f.ChunkHashes
                .Select((h, i) => $"{i,5}  {h}")
                .ToList();
        }
        else
        {
            ChunksHeader.Text = "DNA not available — file may not have been indexed yet.";
        }
    }
}
