using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChiptuningAi.Client;
using ChiptuningAi.Client.Files;
using ChiptuningAi.Client.Patches;
using ChiptuningAi.Dashboard.Services;
using Microsoft.Win32;

namespace ChiptuningAi.Dashboard;

public partial class ProcessingWindow : Window
{
    private readonly ChiptuningAiClient _client;
    private readonly string _filePath;
    private readonly IReadOnlyList<SimilarFile> _parents;
    private readonly TraceFile? _restoredTrace;

    // Selected patches, keyed by PatchId — order is determined by JaccardScore then insertion
    private readonly Dictionary<Guid, SelectedItem> _selected = [];

    // ── Maximize support (same pattern as MainWindow) ─────────────────────────

    private bool _isManuallyMaximized;
    private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;

    private sealed record SelectedItem(
        Guid PatchId,
        Guid ParentFileId,
        string ParentFileName,
        double JaccardScore,
        string? Description,
        string? Version);

    // ── Constructor: fresh from similarity search ──────────────────────────────

    public ProcessingWindow(
        ChiptuningAiClient client,
        string filePath,
        IReadOnlyList<SimilarFile> parents)
    {
        InitializeComponent();
        _client  = client;
        _filePath = filePath;
        _parents  = parents;
        _restoredTrace = null;

        TitleFileName.Text = Path.GetFileName(filePath);
        OutputPathBox.Text = BuildDefaultOutputPath(filePath);

        Loaded += async (_, _) => await LoadPatchesAsync(preSelectedIds: null);
    }

    // ── Constructor: restore from .ctatrace ──────────────────────────────────

    public ProcessingWindow(
        ChiptuningAiClient client,
        string filePath,
        TraceFile trace)
    {
        InitializeComponent();
        _client  = client;
        _filePath = filePath;
        _restoredTrace = trace;

        // Reconstruct SimilarFile list from trace
        _parents = trace.Parents
            .Select(p => new SimilarFile
            {
                FileId         = p.ParentFileId,
                FileName       = p.FileName,
                JaccardScore   = p.JaccardScore,
                MatchPercentage = p.MatchPercentage,
            })
            .ToList();

        TitleFileName.Text = Path.GetFileName(filePath);
        OutputPathBox.Text = BuildDefaultOutputPath(filePath);

        if (trace.BypassIntegrity)
        {
            BypassCheck.IsChecked         = true;
            BypassReasonPanel.Visibility  = Visibility.Visible;
            BypassReasonBox.Text          = trace.BypassReason ?? "";
        }

        var preSelected = trace.SelectedPatches.Select(p => p.PatchId).ToHashSet();
        Loaded += async (_, _) => await LoadPatchesAsync(preSelected);
    }

    // ── Patch loading ─────────────────────────────────────────────────────────

    private async Task LoadPatchesAsync(HashSet<Guid>? preSelectedIds)
    {
        try
        {
            foreach (var parent in _parents.Take(10))
            {
                ChiptuningAi.Client.Common.PagedResult<PatchDetails> page;
                try
                {
                    page = await _client.Patches.ListAsync(parent.FileId, skip: 0, take: 50);
                }
                catch
                {
                    continue;
                }

                if (page.Items.Count == 0) continue;

                var card = BuildParentCard(parent, page.Items, preSelectedIds);
                ParentList.Children.Add(card);
            }
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            RefreshApplyButton();
        }
    }

    private UIElement BuildParentCard(
        SimilarFile parent,
        IReadOnlyList<PatchDetails> patches,
        HashSet<Guid>? preSelected)
    {
        var card = new Border
        {
            Background   = (Brush)Application.Current.Resources["BgMid"],
            Padding      = new Thickness(14, 12, 14, 12),
            Margin       = new Thickness(0, 0, 0, 10),
        };

        var stack = new StackPanel();

        // Header row
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameTb = new TextBlock
        {
            Text       = parent.FileName,
            FontSize   = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(nameTb, 0);

        var pct = parent.MatchPercentage;
        var badgeFg = parent.JaccardScore >= 0.9 ? "#22c55e" :
                      parent.JaccardScore >= 0.7 ? "#eab308" : "#f97316";
        var badge = new Border
        {
            Background  = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeFg + "22")),
            Padding     = new Thickness(8, 3, 8, 3),
            Child       = new TextBlock
            {
                Text       = pct,
                FontSize   = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeFg)),
            },
        };
        Grid.SetColumn(badge, 1);

        header.Children.Add(nameTb);
        header.Children.Add(badge);
        stack.Children.Add(header);

        var divider = new Border
        {
            BorderBrush     = (Brush)Application.Current.Resources["Border"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin          = new Thickness(0, 4, 0, 4),
        };
        stack.Children.Add(divider);

        // Patch checkboxes
        foreach (var patch in patches.Where(p => p.IsActive))
        {
            var label = string.IsNullOrWhiteSpace(patch.Description)
                ? (patch.Version ?? patch.PatchId.ToString("D"))
                : patch.Description + (patch.Version is not null ? $" — {patch.Version}" : "");

            var cb = new CheckBox
            {
                Content    = label,
                FontSize   = 12,
                Foreground = (Brush)Application.Current.Resources["TextPrimary"],
                Margin     = new Thickness(0, 2, 0, 2),
                Tag        = (parent, patch),
                IsChecked  = preSelected?.Contains(patch.PatchId) ?? false,
            };

            cb.Checked   += PatchCheckBox_Changed;
            cb.Unchecked += PatchCheckBox_Changed;

            stack.Children.Add(cb);

            // Pre-select if restoring trace
            if (preSelected?.Contains(patch.PatchId) == true)
                AddSelected(parent, patch);
        }

        card.Child = stack;
        return card;
    }

    // ── Checkbox handling ─────────────────────────────────────────────────────

    private void PatchCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb) return;
        var (parent, patch) = ((SimilarFile, PatchDetails))cb.Tag;

        if (cb.IsChecked == true)
            AddSelected(parent, patch);
        else
            RemoveSelected(patch.PatchId);
    }

    private void AddSelected(SimilarFile parent, PatchDetails patch)
    {
        _selected[patch.PatchId] = new SelectedItem(
            patch.PatchId,
            parent.FileId,
            parent.FileName,
            parent.JaccardScore,
            patch.Description,
            patch.Version);

        RefreshSelectedPanel();
        RefreshApplyButton();
    }

    private void RemoveSelected(Guid patchId)
    {
        _selected.Remove(patchId);
        RefreshSelectedPanel();
        RefreshApplyButton();
    }

    // ── Selected patch panel ──────────────────────────────────────────────────

    private void RefreshSelectedPanel()
    {
        SelectedList.Children.Clear();

        var ordered = _selected.Values
            .OrderByDescending(x => x.JaccardScore)
            .ToList();

        NoSelectionHint.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!SelectedList.Children.Contains(NoSelectionHint))
            SelectedList.Children.Add(NoSelectionHint);

        foreach (var item in ordered)
        {
            var row = new Border
            {
                Background      = (Brush)Application.Current.Resources["BgDark"],
                Padding         = new Thickness(10, 8, 10, 8),
                Margin          = new Thickness(0, 0, 0, 6),
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text         = item.Description ?? item.Version ?? item.PatchId.ToString("D"),
                FontSize     = 12,
                FontWeight   = FontWeights.SemiBold,
                Foreground   = (Brush)Application.Current.Resources["TextPrimary"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(0, 0, 0, 2),
            });
            info.Children.Add(new TextBlock
            {
                Text         = $"{item.ParentFileName} · {item.JaccardScore:P0}",
                FontSize     = 10,
                Foreground   = (Brush)Application.Current.Resources["TextSecondary"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var removeBtn = new Button
            {
                Content     = "✕",
                FontSize    = 12,
                Background  = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground  = (Brush)Application.Current.Resources["TextSecondary"],
                Cursor      = Cursors.Hand,
                Tag         = item.PatchId,
            };
            removeBtn.Click += RemoveBtn_Click;

            Grid.SetColumn(info,      0);
            Grid.SetColumn(removeBtn, 1);
            g.Children.Add(info);
            g.Children.Add(removeBtn);

            row.Child = g;
            SelectedList.Children.Add(row);
        }
    }

    private void RemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Guid patchId) return;

        // Uncheck the corresponding checkbox in the parent list
        foreach (var child in ParentList.Children.OfType<Border>())
        {
            if (child.Child is StackPanel sp)
            {
                foreach (var cb in sp.Children.OfType<CheckBox>())
                {
                    if (cb.Tag is (SimilarFile, PatchDetails pd) && pd.PatchId == patchId)
                    {
                        cb.IsChecked = false;
                        return;
                    }
                }
            }
        }

        RemoveSelected(patchId);
    }

    private void RefreshApplyButton()
        => ApplyBtn.IsEnabled = _selected.Count > 0;

    // ── Bypass integrity toggle ───────────────────────────────────────────────

    private void BypassCheck_Changed(object sender, RoutedEventArgs e)
        => BypassReasonPanel.Visibility = BypassCheck.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

    // ── Output path ───────────────────────────────────────────────────────────

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title            = "Save processed file",
            Filter           = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
            FileName         = Path.GetFileName(OutputPathBox.Text),
            InitialDirectory = Path.GetDirectoryName(_filePath) ?? "",
        };
        if (dlg.ShowDialog() == true)
            OutputPathBox.Text = dlg.FileName;
    }

    private static string BuildDefaultOutputPath(string filePath)
    {
        var dir  = Path.GetDirectoryName(filePath) ?? "";
        var name = Path.GetFileNameWithoutExtension(filePath) + "_processed.bin";
        return Path.Combine(dir, name);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var outputPath = OutputPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            StatusText.Text = LanguageManager.Get("Proc.StatusNoOutput");
            return;
        }

        var bypass       = BypassCheck.IsChecked == true;
        var bypassReason = BypassReasonBox.Text.Trim().NullIfEmpty();

        // Build patch order: sorted by JaccardScore descending (highest similarity applied first)
        var orderedIds = _selected.Values
            .OrderByDescending(x => x.JaccardScore)
            .Select(x => x.PatchId)
            .ToList();

        ApplyBtn.IsEnabled     = false;
        ProgressBar.Visibility = Visibility.Visible;
        StatusText.Text        = LanguageManager.Get("Proc.ApplyingText");

        try
        {
            var resultBytes = await _client.Patches.ProcessAsync(
                _filePath, orderedIds, bypass, bypassReason);

            await File.WriteAllBytesAsync(outputPath, resultBytes);

            SaveTrace(outputPath, orderedIds, bypass, bypassReason);

            StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
            StatusText.Text       = $"Done! Saved to {Path.GetFileName(outputPath)}";
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
            StatusText.Text       = ex.Message.StartsWith("INTEGRITY_MISMATCH")
                ? "Integrity check failed — enable bypass or select a better-matching parent."
                : $"Error: {ex.Message}";
            ApplyBtn.IsEnabled = true;
        }
        finally
        {
            ProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    // ── .ctatrace ────────────────────────────────────────────────────────────

    private void SaveTrace(
        string outputPath,
        List<Guid> orderedIds,
        bool bypass,
        string? bypassReason)
    {
        try
        {
            var trace = new TraceFile
            {
                OriginalFilePath = _filePath,
                OriginalFileName = Path.GetFileName(_filePath),
                BypassIntegrity  = bypass,
                BypassReason     = bypassReason,
                Parents = _parents.Take(10).Select(p => new TraceParent
                {
                    ParentFileId    = p.FileId,
                    FileName        = p.FileName,
                    JaccardScore    = p.JaccardScore,
                    MatchPercentage = p.MatchPercentage,
                }).ToList(),
                SelectedPatches = _selected.Values
                    .OrderByDescending(x => x.JaccardScore)
                    .Select(x => new TraceSelectedPatch
                    {
                        PatchId          = x.PatchId,
                        ParentFileId     = x.ParentFileId,
                        Description      = x.Description,
                        Version          = x.Version,
                        ParentJaccardScore = x.JaccardScore,
                    }).ToList(),
            };
            trace.Save(_filePath);
        }
        catch { /* non-critical */ }
    }

    // ── Window chrome ─────────────────────────────────────────────────────────

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal && _isManuallyMaximized)
        {
            _isManuallyMaximized = false;
            MaxBtn.Content       = "☐";
        }
    }

    private void ToggleMaximize()
    {
        if (_isManuallyMaximized)
        {
            Left   = _restoreLeft;  Top    = _restoreTop;
            Width  = _restoreWidth; Height = _restoreHeight;
            _isManuallyMaximized = false;
            MaxBtn.Content       = "☐";
        }
        else
        {
            _restoreLeft  = Left;  _restoreTop    = Top;
            _restoreWidth = Width; _restoreHeight = Height;
            var wa = SystemParameters.WorkArea;
            Left = wa.Left; Top = wa.Top; Width = wa.Width; Height = wa.Height;
            _isManuallyMaximized = true;
            MaxBtn.Content       = "❐";
        }
    }
}

internal static class StringExtensions
{
    internal static string? NullIfEmpty(this string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;
}
