using SmartViews.Config;
using System.Windows;
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

    private void PopulateControls(ViewConfig config)
    {
        TxtConfigFolder.Text = config.ConfigFolderPath;
        TxtCropOffset.Text = config.CropOffset.ToString("F2");

        CmbDuplicates.ItemsSource = Enum.GetValues<DuplicateHandling>();
        CmbDuplicates.SelectedItem = config.DuplicateHandling;

        // DataGrid binds directly to a copy so cancellation leaves original intact.
        GridViewKinds.ItemsSource = config.ViewKinds
            .Select(k => new ViewKindConfig
            {
                Kind = k.Kind,
                NameTemplate = k.NameTemplate,
                ViewFamilyTypeName = k.ViewFamilyTypeName,
            })
            .ToList();

        // Populate the Kind column's combo items.
        foreach (DataGridComboBoxColumn col in GridViewKinds.Columns.OfType<DataGridComboBoxColumn>())
            col.ItemsSource = Enum.GetValues<ViewKind>();
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select the folder where SmartViews config files are stored",
            SelectedPath = TxtConfigFolder.Text,
            ShowNewFolderButton = true,
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtConfigFolder.Text = dlg.SelectedPath;
    }

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

        if (!double.TryParse(TxtCropOffset.Text, out double offset) || offset < 0)
        {
            System.Windows.MessageBox.Show(
                "Crop offset must be a non-negative number.",
                "SmartViews", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtCropOffset.Focus();
            return false;
        }

        config = new ViewConfig
        {
            ConfigFolderPath = TxtConfigFolder.Text.Trim(),
            CropOffset = offset,
            DuplicateHandling = (DuplicateHandling)(CmbDuplicates.SelectedItem ?? DuplicateHandling.Skip),
            ViewKinds = (GridViewKinds.ItemsSource as IEnumerable<ViewKindConfig>)?.ToList()
                        ?? [],
        };

        return true;
    }
}
