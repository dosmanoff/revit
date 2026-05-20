using SmartViews.Config;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace SmartViews.UI;

public partial class SmartViewsDialog : Window
{
    public ViewConfig Config { get; private set; }

    public SmartViewsDialog(ViewConfig initial)
    {
        InitializeComponent();
        Config = initial;
        PopulateControls(initial);
    }

    // -----------------------------------------------------------------------
    // Population
    // -----------------------------------------------------------------------

    private void PopulateControls(ViewConfig config)
    {
        TxtConfigFolder.Text = config.ConfigFolderPath;
        TxtCropOffset.Text   = config.CropOffset.ToString("F2");

        CmbDuplicates.ItemsSource  = Enum.GetValues<DuplicateHandling>();
        CmbDuplicates.SelectedItem = config.DuplicateHandling;

        // DataGrid rows are copies so cancellation leaves the original intact.
        GridViewKinds.ItemsSource = config.ViewKinds
            .Select(k => new ViewKindConfig
            {
                Kind                 = k.Kind,
                SectionDirection     = k.SectionDirection,
                CreateAllDirections  = k.CreateAllDirections,
                AlignToElement       = k.AlignToElement,
                NameTemplate         = k.NameTemplate,
                ViewFamilyTypeName   = k.ViewFamilyTypeName,
                ViewTemplateName     = k.ViewTemplateName,
            })
            .ToList();

        foreach (DataGridComboBoxColumn col in GridViewKinds.Columns.OfType<DataGridComboBoxColumn>())
        {
            col.ItemsSource = col.Header?.ToString() == "Direction"
                ? (System.Collections.IEnumerable)Enum.GetValues<SectionDirection>()
                : Enum.GetValues<ViewKind>();
        }

        // Plan view range
        bool hasPlanRange = config.PlanViewRange is not null;
        PlanViewRangeConfig pr = config.PlanViewRange ?? new PlanViewRangeConfig();
        TxtPlanTop.Text    = pr.TopOffset.ToString("F2");
        TxtPlanCut.Text    = pr.CutOffset.ToString("F2");
        TxtPlanBottom.Text = pr.BottomOffset.ToString("F2");
        TxtPlanDepth.Text  = pr.ViewDepth.ToString("F2");
        ChkPlanRange.IsChecked = hasPlanRange;
        SetPlanRangeFieldsEnabled(hasPlanRange);

        RefreshPresetList(preserveSelection: true);
    }

    // -----------------------------------------------------------------------
    // Plan view range
    // -----------------------------------------------------------------------

    private void ChkPlanRange_Changed(object sender, RoutedEventArgs e)
        => SetPlanRangeFieldsEnabled(ChkPlanRange.IsChecked == true);

    private void SetPlanRangeFieldsEnabled(bool enabled)
    {
        foreach (TextBox tb in PnlPlanRangeFields.Children.OfType<TextBox>())
            tb.IsEnabled = enabled;
    }

    // -----------------------------------------------------------------------
    // Preset helpers
    // -----------------------------------------------------------------------

    private void RefreshPresetList(bool preserveSelection = false)
    {
        string? current = CmbPreset.Text;
        CmbPreset.ItemsSource = ConfigLoader.ListPresets(TxtConfigFolder.Text.Trim());
        if (preserveSelection && current is not null)
            CmbPreset.Text = current;
    }

    private void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        string folder = TxtConfigFolder.Text.Trim();
        string name   = CmbPreset.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(name))
        {
            System.Windows.MessageBox.Show("Enter or select a preset name first.",
                "SmartViews", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ViewConfig? preset = ConfigLoader.LoadPreset(folder, name);
        if (preset is null)
        {
            System.Windows.MessageBox.Show($"Preset \"{name}\" was not found in the config folder.",
                "SmartViews", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        preset.ConfigFolderPath = folder;
        PopulateControls(preset);
        CmbPreset.Text = name;
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        string folder = TxtConfigFolder.Text.Trim();
        string name   = CmbPreset.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(folder))
        {
            System.Windows.MessageBox.Show("Set a config folder path before saving a preset.",
                "SmartViews", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrEmpty(name))
        {
            System.Windows.MessageBox.Show("Enter a preset name first.",
                "SmartViews", MessageBoxButton.OK, MessageBoxImage.Information);
            CmbPreset.Focus();
            return;
        }

        if (!TryCollectConfig(out ViewConfig? config))
            return;

        try
        {
            ConfigLoader.SavePreset(folder, name, config!);
            RefreshPresetList(preserveSelection: true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not save preset: {ex.Message}",
                "SmartViews", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -----------------------------------------------------------------------
    // Folder browse / text change
    // -----------------------------------------------------------------------

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description         = "Select the folder where SmartViews config files are stored",
            SelectedPath        = TxtConfigFolder.Text,
            ShowNewFolderButton = true,
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TxtConfigFolder.Text = dlg.SelectedPath;
            RefreshPresetList();
        }
    }

    private void TxtConfigFolder_LostFocus(object sender, RoutedEventArgs e)
        => RefreshPresetList();

    // -----------------------------------------------------------------------
    // OK
    // -----------------------------------------------------------------------

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCollectConfig(out ViewConfig? config))
            return;

        Config = config!;
        DialogResult = true;
    }

    private bool TryCollectConfig(out ViewConfig? config)
    {
        config = null;

        if (!TryParseNonNegative(TxtCropOffset.Text, out double offset))
        {
            System.Windows.MessageBox.Show("Crop offset must be a non-negative number.",
                "SmartViews", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtCropOffset.Focus();
            return false;
        }

        PlanViewRangeConfig? planRange = null;
        if (ChkPlanRange.IsChecked == true)
        {
            if (!TryParseDouble(TxtPlanTop.Text,    out double top)    ||
                !TryParseDouble(TxtPlanCut.Text,    out double cut)    ||
                !TryParseDouble(TxtPlanBottom.Text, out double bottom) ||
                !TryParseDouble(TxtPlanDepth.Text,  out double depth))
            {
                System.Windows.MessageBox.Show(
                    "All plan view range offsets must be valid numbers.",
                    "SmartViews", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPlanTop.Focus();
                return false;
            }

            planRange = new PlanViewRangeConfig
            {
                TopOffset    = top,
                CutOffset    = cut,
                BottomOffset = bottom,
                ViewDepth    = depth,
            };
        }

        config = new ViewConfig
        {
            ConfigFolderPath  = TxtConfigFolder.Text.Trim(),
            CropOffset        = offset,
            DuplicateHandling = (DuplicateHandling)(CmbDuplicates.SelectedItem ?? DuplicateHandling.Skip),
            PlanViewRange     = planRange,
            ViewKinds         = (GridViewKinds.ItemsSource as IEnumerable<ViewKindConfig>)?.ToList() ?? [],
        };

        return true;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static bool TryParseNonNegative(string text, out double value) =>
        TryParseDouble(text, out value) && value >= 0;
}
