using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WallReinforcement.Geometry;
using WpfGrid = System.Windows.Controls.Grid;

namespace WallReinforcement.UI;

/// <summary>
/// Standalone ACI 318-19 reference calculator: enter f'c / fy and the modifiers, and read the
/// tension development length ℓd (§25.4.2.3) and Class B tension lap splice ℓst (§25.5.2.1) for
/// every ASTM bar size. The same numbers Wall Reinforcement uses when *Anchorage → Use ACI* is on.
/// Code-only WPF (no XAML), matching the rest of the plugin.
/// </summary>
public class AciLengthsDialog : Window
{
    private TextBox _txtFc = null!;
    private TextBox _txtFy = null!;
    private CheckBox _chkTop = null!;
    private CheckBox _chkEpoxy = null!;
    private CheckBox _chkLight = null!;
    private CheckBox _chkAdeq = null!;
    private StackPanel _results = null!;
    private TextBlock _err = null!;

    public AciLengthsDialog()
    {
        Title = "ACI 318-19 Anchorage Lengths";
        Width = 560;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new WpfGrid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(root, 0, BuildInputs());
        AddRow(root, 1, BuildModifiers());

        _err = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap };
        AddRow(root, 2, _err);

        _results = new StackPanel { Orientation = Orientation.Vertical };
        AddRow(root, 3, new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _results });

        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, MinWidth = 90, Padding = new Thickness(12, 4, 12, 4) };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        btns.Children.Add(close);
        AddRow(root, 4, btns);

        Content = root;
        Recompute();
    }

    private static void AddRow(WpfGrid g, int row, UIElement child) { WpfGrid.SetRow(child, row); g.Children.Add(child); }

    private UIElement BuildInputs()
    {
        _txtFc = new TextBox { Text = "4000", Width = 90, Padding = new Thickness(4, 2, 4, 2) };
        _txtFy = new TextBox { Text = "60000", Width = 90, Padding = new Thickness(4, 2, 4, 2) };
        _txtFc.TextChanged += (_, _) => Recompute();
        _txtFy.TextChanged += (_, _) => Recompute();

        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        p.Children.Add(new Label { Content = "f'c (psi):", VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(_txtFc);
        p.Children.Add(new Label { Content = "   fy (psi):", VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(_txtFy);
        return p;
    }

    private UIElement BuildModifiers()
    {
        _chkTop   = MkCheck("Top bar (ψt)");
        _chkEpoxy = MkCheck("Epoxy (ψe)");
        _chkLight = MkCheck("Lightweight (λ)");
        _chkAdeq  = MkCheck("Adequate spacing/cover");
        _chkAdeq.IsChecked = true;

        var p = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        p.Children.Add(_chkTop);
        p.Children.Add(_chkEpoxy);
        p.Children.Add(_chkLight);
        p.Children.Add(_chkAdeq);
        return p;
    }

    private CheckBox MkCheck(string text)
    {
        var cb = new CheckBox { Content = text, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        cb.Checked += (_, _) => Recompute();
        cb.Unchecked += (_, _) => Recompute();
        return cb;
    }

    private void Recompute()
    {
        if (_results is null) return;   // not built yet
        _results.Children.Clear();
        _err.Text = "";

        if (!double.TryParse(_txtFc.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double fc) || fc <= 0 ||
            !double.TryParse(_txtFy.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double fy) || fy <= 0)
        {
            _err.Text = "Enter positive numbers for f'c and fy.";
            return;
        }

        _results.Children.Add(HeaderRow());
        foreach (string bar in AciAnchorageCalculator.BarDiameters.Keys.OrderBy(NumOf))
        {
            var inputs = new AciAnchorageCalculator.Inputs
            {
                BarSize = bar, FcPsi = fc, FyPsi = fy,
                IsTopBar = _chkTop.IsChecked == true,
                IsEpoxyCoated = _chkEpoxy.IsChecked == true,
                IsLightweight = _chkLight.IsChecked == true,
                AdequateSpacing = _chkAdeq.IsChecked == true,
            };
            double ld = AciAnchorageCalculator.DevelopmentLengthTensionIn(inputs);
            double lap = AciAnchorageCalculator.TensionLapSpliceClassBIn(inputs);
            _results.Children.Add(DataRow(
                bar,
                AciAnchorageCalculator.DiameterIn(bar).ToString("0.###", CultureInfo.InvariantCulture),
                $"{ld:0.0}  ({FtIn(ld)})",
                $"{lap:0.0}  ({FtIn(lap)})"));
        }
    }

    private static int NumOf(string bar) => int.TryParse(bar.TrimStart('#'), out int n) ? n : 999;

    private UIElement HeaderRow() => Row("Bar", "db (in)", "ℓd  dev (in)", "ℓst  lap (in)", bold: true);
    private UIElement DataRow(string a, string b, string c, string d) => Row(a, b, c, d, bold: false);

    private UIElement Row(string a, string b, string c, string d, bool bold)
    {
        var g = new WpfGrid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        FontWeight fw = bold ? FontWeights.SemiBold : FontWeights.Normal;
        Cell(g, 0, a, fw); Cell(g, 1, b, fw); Cell(g, 2, c, fw); Cell(g, 3, d, fw);
        return g;
    }

    private static void Cell(WpfGrid g, int col, string text, FontWeight fw)
    {
        var t = new TextBlock { Text = text, FontWeight = fw, Margin = new Thickness(2, 0, 2, 0) };
        WpfGrid.SetColumn(t, col);
        g.Children.Add(t);
    }

    /// <summary>Inches → architectural feet-inches rounded to the nearest ¼", e.g. 23.7 → 1'-11 3/4".</summary>
    private static string FtIn(double inches)
    {
        int quarters = (int)Math.Round(inches * 4);
        int feet = quarters / 48;
        int remQ = quarters % 48;
        int whole = remQ / 4;
        int frac = remQ % 4;
        string fracStr = frac switch { 1 => " 1/4", 2 => " 1/2", 3 => " 3/4", _ => "" };
        return $"{feet}'-{whole}{fracStr}\"";
    }
}
