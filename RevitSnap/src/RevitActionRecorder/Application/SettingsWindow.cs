using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using RevitActionRecorder.Configuration;

namespace RevitActionRecorder.Application;

/// <summary>Окно Settings: правит config.json рядом со сборкой.</summary>
internal sealed class SettingsWindow : Window
{
    private readonly RecorderConfig _config;
    private readonly TextBox _outputRoot;
    private readonly TextBox _views;
    private readonly TextBox _pixelSize;
    private readonly TextBox _correlationMs;
    private readonly CheckBox _gzip;
    private readonly CheckBox _geometryHash;
    private readonly CheckBox _elementOverrides;
    private readonly ComboBox _logLevel;

    public SettingsWindow(RecorderConfig config, IntPtr ownerHandle)
    {
        _config = config;
        Title = "RevitActionRecorder — Settings";
        Width = 560;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(14) };

        panel.Children.Add(Label("Корневая папка вывода:"));
        var rootRow = new DockPanel();
        var browse = new Button { Content = "…", Width = 32, Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        _outputRoot = new TextBox { Text = config.OutputRoot };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { InitialDirectory = _outputRoot.Text };
            if (dialog.ShowDialog(this) == true)
                _outputRoot.Text = dialog.FolderName;
        };
        rootRow.Children.Add(browse);
        rootRow.Children.Add(_outputRoot);
        panel.Children.Add(rootRow);

        panel.Children.Add(Label("Виды для PNG (по одному на строку; точное имя или regex; пусто = активный вид):"));
        _views = new TextBox
        {
            AcceptsReturn = true,
            Height = 110,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join(Environment.NewLine, config.SnapshotViews),
        };
        panel.Children.Add(_views);

        panel.Children.Add(Label("Разрешение PNG, px (большая сторона):"));
        _pixelSize = new TextBox { Text = config.ImagePixelSize.ToString(CultureInfo.InvariantCulture), Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(_pixelSize);

        panel.Children.Add(Label("Порог корреляции команды и транзакции, мс:"));
        _correlationMs = new TextBox { Text = config.CommandCorrelationMs.ToString(CultureInfo.InvariantCulture), Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(_correlationMs);

        _gzip = new CheckBox { Content = "Сжимать снапшоты (gzip)", IsChecked = config.Gzip, Margin = new Thickness(0, 10, 0, 0) };
        _geometryHash = new CheckBox { Content = "Хеш геометрии в снапшоте (медленнее, но видно «геометрия изменилась»)", IsChecked = config.GeometryHash, Margin = new Thickness(0, 6, 0, 0) };
        _elementOverrides = new CheckBox { Content = "Собирать переопределения графики по элементам в каждом виде (медленно на больших моделях)", IsChecked = config.CollectElementOverrides, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(_gzip);
        panel.Children.Add(_geometryHash);
        panel.Children.Add(_elementOverrides);

        panel.Children.Add(Label("Уровень логирования:"));
        _logLevel = new ComboBox { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var level in new[] { "Debug", "Info", "Warn", "Error" })
            _logLevel.Items.Add(level);
        _logLevel.SelectedItem = _logLevel.Items.Contains(config.LogLevel) ? config.LogLevel : "Info";
        panel.Children.Add(_logLevel);

        var save = new Button { Content = "Сохранить", Width = 110, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Отмена", Width = 90, IsCancel = true };
        save.Click += (_, _) => Save();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        if (ownerHandle != IntPtr.Zero)
            new WindowInteropHelper(this) { Owner = ownerHandle };
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, Margin = new Thickness(0, 10, 0, 4), TextWrapping = TextWrapping.Wrap };

    private void Save()
    {
        _config.OutputRoot = string.IsNullOrWhiteSpace(_outputRoot.Text) ? _config.OutputRoot : _outputRoot.Text.Trim();
        _config.SnapshotViews = _views.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (int.TryParse(_pixelSize.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pixels) && pixels > 0)
            _config.ImagePixelSize = pixels;
        if (int.TryParse(_correlationMs.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms > 0)
            _config.CommandCorrelationMs = ms;
        _config.Gzip = _gzip.IsChecked == true;
        _config.GeometryHash = _geometryHash.IsChecked == true;
        _config.CollectElementOverrides = _elementOverrides.IsChecked == true;
        _config.LogLevel = _logLevel.SelectedItem as string ?? "Info";

        _config.Save();
        DialogResult = true;
    }
}
