using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WeChatSendGuard.Models;
using WeChatSendGuard.Services;

namespace WeChatSendGuard;

/// <summary>
/// 配置界面：在 <see cref="GuardConfig"/> 的副本上编辑，点"保存并应用"才写回 config.json 并热重载；
/// 点"取消"或直接关窗丢弃全部改动。
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly Regex ColorPattern = new("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

    private static readonly RegionSpec DefaultRegion = new(300, 33, 580, 30);
    private const int DefaultScale = 2;

    private readonly GuardConfig _config;
    private readonly string _configPath;
    private readonly Action<GuardConfig> _onApply;

    private readonly ObservableCollection<RiskChatEntry> _entries = new();

    private RiskChatEntry? _currentEntry;

    /// <summary>批量填充控件时置位，避免 SelectionChanged / 写回逻辑被误触发。</summary>
    private bool _loading;

    public SettingsWindow(GuardConfig config, string configPath, Action<GuardConfig> onApply)
    {
        InitializeComponent();

        _config = ConfigService.Clone(config);
        _configPath = configPath;
        _onApply = onApply;

        ConfigPathText.Text = $"配置文件：{configPath}";
        EntryList.ItemsSource = _entries;

        LoadFromConfig();
    }

    // ---------- 加载 ----------

    private void LoadFromConfig()
    {
        _loading = true;

        EnabledBox.IsChecked = _config.Enabled;
        WindowPollBox.Text = _config.WindowPollMs.ToString();
        OcrIntervalBox.Text = _config.OcrIntervalMs.ToString();
        DebounceBox.Text = _config.DebounceCount.ToString();

        _entries.Clear();
        foreach (var entry in _config.RiskChats)
        {
            _entries.Add(entry);
        }

        DefaultModeContains.IsChecked = _config.Matching.DefaultMode == MatchMode.Contains;
        DefaultModeExact.IsChecked = _config.Matching.DefaultMode == MatchMode.Exact;
        DefaultModeRegex.IsChecked = _config.Matching.DefaultMode == MatchMode.Regex;
        if (DefaultModeContains.IsChecked != true && DefaultModeExact.IsChecked != true && DefaultModeRegex.IsChecked != true)
        {
            DefaultModeContains.IsChecked = true;
        }

        CaseSensitiveBox.IsChecked = _config.Matching.CaseSensitive;

        BorderEnabledBox.IsChecked = _config.Border.Enabled;
        BorderColorBox.Text = _config.Border.Color;
        BorderThicknessBox.Text = _config.Border.Thickness.ToString("0.##");
        BorderShowLabelBox.IsChecked = _config.Border.ShowLabel;
        BorderLabelFormatBox.Text = _config.Border.LabelFormat;

        GifEnabledBox.IsChecked = _config.Gif.Enabled;
        GifFileBox.Text = _config.Gif.File;
        GifMaxSizeBox.Text = _config.Gif.MaxSize.ToString();
        GifOffsetXBox.Text = _config.Gif.OffsetX.ToString();
        GifOffsetYBox.Text = _config.Gif.OffsetY.ToString();

        RegionXBox.Text = _config.Ocr.BaseRegion.X.ToString();
        RegionYBox.Text = _config.Ocr.BaseRegion.Y.ToString();
        RegionWBox.Text = _config.Ocr.BaseRegion.Width.ToString();
        RegionHBox.Text = _config.Ocr.BaseRegion.Height.ToString();
        BaseScaleBox.Text = _config.Ocr.BaseScale.ToString();
        NormalizeBox.IsChecked = _config.Ocr.NormalizeByDpi;
        DumpBox.Text = _config.Ocr.DumpDirectory;

        _loading = false;

        EntryList.SelectedIndex = _entries.Count > 0 ? 0 : -1;
        if (_entries.Count == 0)
        {
            LoadEntryToEditor(null);
        }
    }

    // ---------- 名单编辑 ----------

    private void OnEntrySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        CommitEditorToEntry(_currentEntry);

        _currentEntry = EntryList.SelectedItem as RiskChatEntry;
        LoadEntryToEditor(_currentEntry);
    }

    private void LoadEntryToEditor(RiskChatEntry? entry)
    {
        _loading = true;

        EntryEditor.IsEnabled = entry is not null;

        EntryNameBox.Text = entry?.Name ?? string.Empty;
        EntryNoteBox.Text = entry?.Note ?? string.Empty;

        var mode = entry?.Mode ?? MatchMode.Default;
        EntryModeContains.IsChecked = mode == MatchMode.Contains;
        EntryModeExact.IsChecked = mode == MatchMode.Exact;
        EntryModeRegex.IsChecked = mode == MatchMode.Regex;
        EntryModeInherit.IsChecked = mode == MatchMode.Default;

        _loading = false;
    }

    /// <summary>把编辑器里的内容写回条目对象（名称变化需刷新列表显示）。</summary>
    private void CommitEditorToEntry(RiskChatEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var name = EntryNameBox.Text.Trim();
        var note = EntryNoteBox.Text.Trim();

        var changed = entry.Name != name;
        entry.Name = name;
        entry.Note = string.IsNullOrEmpty(note) ? null : note;
        entry.Mode = SelectedEntryMode();

        if (changed)
        {
            RefreshEntryList();
        }
    }

    private MatchMode SelectedEntryMode()
    {
        if (EntryModeExact.IsChecked == true)
        {
            return MatchMode.Exact;
        }

        if (EntryModeRegex.IsChecked == true)
        {
            return MatchMode.Regex;
        }

        if (EntryModeInherit.IsChecked == true)
        {
            return MatchMode.Default;
        }

        return MatchMode.Contains;
    }

    private MatchMode SelectedDefaultMode()
    {
        if (DefaultModeExact.IsChecked == true)
        {
            return MatchMode.Exact;
        }

        if (DefaultModeRegex.IsChecked == true)
        {
            return MatchMode.Regex;
        }

        return MatchMode.Contains;
    }

    /// <summary>ListBox 用 DisplayMemberPath 绑定，名称变更后需手动刷新才会更新显示。</summary>
    private void RefreshEntryList()
    {
        _loading = true;
        EntryList.Items.Refresh();
        _loading = false;
    }

    private void OnAddEntryClick(object sender, RoutedEventArgs e)
    {
        CommitEditorToEntry(_currentEntry);

        var entry = new RiskChatEntry { Name = "新会话", Mode = MatchMode.Default };
        _entries.Add(entry);
        RefreshEntryList();
        EntryList.SelectedItem = entry;
        EntryList.ScrollIntoView(entry);
        EntryNameBox.Focus();
        EntryNameBox.SelectAll();
    }

    private void OnRemoveEntryClick(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not RiskChatEntry entry)
        {
            return;
        }

        var index = _entries.IndexOf(entry);
        _entries.Remove(entry);
        _currentEntry = null;
        RefreshEntryList();

        if (_entries.Count > 0)
        {
            EntryList.SelectedIndex = Math.Clamp(index, 0, _entries.Count - 1);
        }
        else
        {
            LoadEntryToEditor(null);
        }
    }

    // ---------- 浏览 ----------

    private void OnBrowseGifClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 GIF 文件",
            Filter = "GIF 图片 (*.gif)|*.gif|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            GifFileBox.Text = dialog.FileName;
        }
    }

    private void OnBrowseDumpClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择裁剪小图落盘目录",
        };

        if (dialog.ShowDialog(this) == true)
        {
            DumpBox.Text = dialog.FolderName;
        }
    }

    private void OnResetOcrClick(object sender, RoutedEventArgs e)
    {
        RegionXBox.Text = DefaultRegion.X.ToString();
        RegionYBox.Text = DefaultRegion.Y.ToString();
        RegionWBox.Text = DefaultRegion.Width.ToString();
        RegionHBox.Text = DefaultRegion.Height.ToString();
        BaseScaleBox.Text = DefaultScale.ToString();
        NormalizeBox.IsChecked = true;
    }

    // ---------- 保存 / 取消 ----------

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        CommitEditorToEntry(_currentEntry);

        if (!TryReadInt(WindowPollBox, 50, 5000, "窗口轮询", out var windowPoll)
            || !TryReadInt(OcrIntervalBox, 500, 5000, "识别间隔", out var ocrInterval)
            || !TryReadInt(DebounceBox, 1, 10, "去抖次数", out var debounce)
            || !TryReadDouble(BorderThicknessBox, 1, 40, "红框粗细", out var thickness)
            || !TryReadColor(BorderColorBox.Text, out var borderColor)
            || !TryReadInt(GifMaxSizeBox, 16, 512, "GIF 最大尺寸", out var gifMaxSize)
            || !TryReadInt(GifOffsetXBox, -4000, 4000, "GIF 偏移 X", out var gifOffsetX)
            || !TryReadInt(GifOffsetYBox, -4000, 4000, "GIF 偏移 Y", out var gifOffsetY)
            || !TryReadInt(RegionXBox, 0, 8000, "裁剪区 x", out var regionX)
            || !TryReadInt(RegionYBox, 0, 8000, "裁剪区 y", out var regionY)
            || !TryReadInt(RegionWBox, 1, 8000, "裁剪区 width", out var regionWidth)
            || !TryReadInt(RegionHBox, 1, 8000, "裁剪区 height", out var regionHeight)
            || !TryReadInt(BaseScaleBox, 1, 8, "基准放大倍数", out var baseScale))
        {
            return;
        }

        _config.Enabled = EnabledBox.IsChecked == true;
        _config.WindowPollMs = windowPoll;
        _config.OcrIntervalMs = ocrInterval;
        _config.DebounceCount = debounce;

        _config.RiskChats = _entries.Where(item => !string.IsNullOrWhiteSpace(item.Name)).ToList();
        _config.Matching.DefaultMode = SelectedDefaultMode();
        _config.Matching.CaseSensitive = CaseSensitiveBox.IsChecked == true;

        _config.Border.Enabled = BorderEnabledBox.IsChecked == true;
        _config.Border.Color = borderColor;
        _config.Border.Thickness = thickness;
        _config.Border.ShowLabel = BorderShowLabelBox.IsChecked == true;
        _config.Border.LabelFormat = string.IsNullOrWhiteSpace(BorderLabelFormatBox.Text)
            ? "⚠ 风险聊天：{0}"
            : BorderLabelFormatBox.Text;

        _config.Gif.Enabled = GifEnabledBox.IsChecked == true;
        _config.Gif.File = GifFileBox.Text.Trim();
        _config.Gif.MaxSize = gifMaxSize;
        _config.Gif.OffsetX = gifOffsetX;
        _config.Gif.OffsetY = gifOffsetY;

        _config.Ocr.BaseRegion = new RegionSpec(regionX, regionY, regionWidth, regionHeight);
        _config.Ocr.BaseScale = baseScale;
        _config.Ocr.NormalizeByDpi = NormalizeBox.IsChecked == true;
        _config.Ocr.DumpDirectory = DumpBox.Text.Trim();

        try
        {
            ConfigService.Save(_config, _configPath);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"保存失败：{exception.Message}", "保存配置",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _onApply(_config);
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    // ---------- 输入校验 ----------

    private bool TryReadInt(TextBox box, int min, int max, string label, out int value)
    {
        if (int.TryParse(box.Text.Trim(), out value) && value >= min && value <= max)
        {
            return true;
        }

        Warn($"{label} 需为 {min}~{max} 之间的整数。", box);
        value = min;
        return false;
    }

    private bool TryReadDouble(TextBox box, double min, double max, string label, out double value)
    {
        if (double.TryParse(box.Text.Trim(), out value) && value >= min && value <= max)
        {
            return true;
        }

        Warn($"{label} 需为 {min}~{max} 之间的数值。", box);
        value = min;
        return false;
    }

    private bool TryReadColor(string text, out string color)
    {
        color = text.Trim();
        if (ColorPattern.IsMatch(color))
        {
            return true;
        }

        Warn("红框颜色需为 #RRGGBB 或 #AARRGGBB 格式。", BorderColorBox);
        color = "#E53935";
        return false;
    }

    private void Warn(string message, TextBox box)
    {
        MessageBox.Show(this, message, "输入有误", MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        box.SelectAll();
    }
}
