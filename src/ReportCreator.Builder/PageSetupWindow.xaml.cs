using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ReportCreator.Model;

namespace ReportCreator.Builder;

public partial class PageSetupWindow : Window
{
    private readonly PageSetup _page;

    public PageSetupWindow(PageSetup page)
    {
        InitializeComponent();
        _page = Clone(page);
        PaperBox.ItemsSource = Enum.GetValues<PaperKind>();
        PaperBox.SelectedItem = _page.Paper;
        OrientationBox.ItemsSource = Enum.GetValues<PageOrientation>();
        OrientationBox.SelectedItem = _page.Orientation;
        WidthBox.Text = (_page.CustomWidthMm ?? 210).ToString(CultureInfo.CurrentCulture);
        HeightBox.Text = (_page.CustomHeightMm ?? 297).ToString(CultureInfo.CurrentCulture);
        TopBox.Text = _page.Margin.Top.ToString(CultureInfo.CurrentCulture);
        RightBox.Text = _page.Margin.Right.ToString(CultureInfo.CurrentCulture);
        BottomBox.Text = _page.Margin.Bottom.ToString(CultureInfo.CurrentCulture);
        LeftBox.Text = _page.Margin.Left.ToString(CultureInfo.CurrentCulture);
        HeaderBox.Text = _page.PageHeaderMm.ToString(CultureInfo.CurrentCulture);
        FooterBox.Text = _page.PageFooterMm.ToString(CultureInfo.CurrentCulture);
        NumberBox.ItemsSource = Enum.GetValues<PageNumberPlacement>();
        NumberBox.SelectedItem = _page.PageNumbers;
        FirstNumberBox.IsChecked = !_page.OmitPageNumberOnFirstPage;
        UpdateCustom();
    }

    public PageSetup Page => _page;

    private void PaperBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCustom();

    private void UpdateCustom()
    {
        var custom = PaperBox.SelectedItem is PaperKind.Custom;
        WidthBox.IsEnabled = custom;
        HeightBox.IsEnabled = custom;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (PaperBox.SelectedItem is not PaperKind paper || OrientationBox.SelectedItem is not PageOrientation orientation)
            return;

        if (!TryMm(TopBox.Text, out var top) || !TryMm(RightBox.Text, out var right)
            || !TryMm(BottomBox.Text, out var bottom) || !TryMm(LeftBox.Text, out var left)
            || !TryMm(HeaderBox.Text, out var header) || !TryMm(FooterBox.Text, out var footer))
        {
            MessageBox.Show(this, "Margins and page chrome heights must be numbers, in millimetres.", "Page setup");
            return;
        }

        if (NumberBox.SelectedItem is not PageNumberPlacement numbers)
            return;

        decimal? width = null;
        decimal? height = null;
        if (paper == PaperKind.Custom)
        {
            if (!TryMm(WidthBox.Text, out var customWidth) || !TryMm(HeightBox.Text, out var customHeight))
            {
                MessageBox.Show(this, "Custom width and height must be numbers, in millimetres.", "Page setup");
                return;
            }

            width = customWidth;
            height = customHeight;
        }

        _page.Paper = paper;
        _page.Orientation = orientation;
        _page.CustomWidthMm = width;
        _page.CustomHeightMm = height;
        _page.Margin.Top = top;
        _page.Margin.Right = right;
        _page.Margin.Bottom = bottom;
        _page.Margin.Left = left;
        _page.PageHeaderMm = header;
        _page.PageFooterMm = footer;
        _page.PageNumbers = numbers;
        _page.OmitPageNumberOnFirstPage = FirstNumberBox.IsChecked != true;

        var errors = ReportValidator.Validate(new ReportDefinition { Name = "Page", Page = _page });
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Page setup");
            return;
        }

        DialogResult = true;
    }

    private static bool TryMm(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static PageSetup Clone(PageSetup page) => new()
    {
        Paper = page.Paper,
        Orientation = page.Orientation,
        CustomWidthMm = page.CustomWidthMm,
        CustomHeightMm = page.CustomHeightMm,
        Margin = new Margin
        {
            Top = page.Margin.Top,
            Right = page.Margin.Right,
            Bottom = page.Margin.Bottom,
            Left = page.Margin.Left
        },
        PageHeaderMm = page.PageHeaderMm,
        PageFooterMm = page.PageFooterMm,
        PageNumbers = page.PageNumbers,
        OmitPageNumberOnFirstPage = page.OmitPageNumberOnFirstPage
    };
}
