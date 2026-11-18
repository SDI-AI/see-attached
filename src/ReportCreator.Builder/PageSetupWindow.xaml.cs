using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ReportCreator.Model;

namespace ReportCreator.Builder;

public partial class PageSetupWindow : Window
{
    private static readonly string[] Fonts = ["", "Source Sans 3", "Arial", "Segoe UI", "Times New Roman", "Georgia", "Courier New"];

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
        var mark = _page.Watermark;
        WatermarkBox.IsChecked = mark is { Enabled: true };
        KindBox.ItemsSource = Enum.GetValues<WatermarkKind>();
        KindBox.SelectedItem = mark?.Kind ?? WatermarkKind.Text;
        MarkTextBox.Text = mark?.Text ?? "";
        FontBox.ItemsSource = Fonts;
        FontBox.SelectedItem = Fonts.Contains(mark?.FontFamily ?? "") ? mark?.FontFamily ?? "" : "";
        SizeBox.Text = (mark?.FontSizePt ?? 72).ToString(CultureInfo.CurrentCulture);
        ColorBox.Text = mark?.Color is { Length: > 0 } color ? color : "#C0C0C0";
        OpacityBox.Text = mark?.Opacity?.ToString(CultureInfo.CurrentCulture) ?? "";
        RotationBox.Text = (mark?.RotationDegrees ?? 0).ToString(CultureInfo.CurrentCulture);
        HorizontalBox.ItemsSource = Enum.GetValues<WatermarkHorizontal>();
        HorizontalBox.SelectedItem = mark?.Horizontal ?? WatermarkHorizontal.Center;
        VerticalBox.ItemsSource = Enum.GetValues<WatermarkVertical>();
        VerticalBox.SelectedItem = mark?.Vertical ?? WatermarkVertical.Center;
        SourceBox.Text = mark?.Source ?? "";
        ImageWidthBox.Text = mark?.WidthMm?.ToString(CultureInfo.CurrentCulture) ?? "";
        ImageHeightBox.Text = mark?.HeightMm?.ToString(CultureInfo.CurrentCulture) ?? "";
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

        if (KindBox.SelectedItem is not WatermarkKind kind
            || HorizontalBox.SelectedItem is not WatermarkHorizontal horizontal
            || VerticalBox.SelectedItem is not WatermarkVertical vertical)
            return;

        if (!TryMm(SizeBox.Text, out var fontSize) || !TryMm(RotationBox.Text, out var rotation)
            || !TryOptional(OpacityBox.Text, out var opacity)
            || !TryOptional(ImageWidthBox.Text, out var imageWidth)
            || !TryOptional(ImageHeightBox.Text, out var imageHeight))
        {
            MessageBox.Show(this, "Watermark size, opacity, rotation, and image size must be numbers. Leave opacity and image size blank to omit them.", "Page setup");
            return;
        }

        var markText = MarkTextBox.Text.Trim();
        var source = SourceBox.Text.Trim();
        var enabled = WatermarkBox.IsChecked == true;
        PageWatermark? watermark = null;
        if (enabled || markText.Length > 0 || source.Length > 0)
        {
            var color = ColorBox.Text.Trim();
            watermark = new PageWatermark
            {
                Enabled = enabled,
                Kind = kind,
                Text = markText,
                FontFamily = FontBox.SelectedItem is string family && family.Length > 0 ? family : null,
                FontSizePt = fontSize,
                Color = color.Length == 0 ? null : color,
                Opacity = opacity,
                RotationDegrees = rotation,
                Horizontal = horizontal,
                Vertical = vertical,
                Source = source.Length == 0 ? null : source,
                WidthMm = imageWidth,
                HeightMm = imageHeight
            };
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
        _page.Watermark = watermark;

        var errors = ReportValidator.Validate(new ReportDefinition { Name = "Page", Page = _page });
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Page setup");
            return;
        }

        DialogResult = true;
    }

    private static bool TryOptional(string text, out decimal? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (!TryMm(text, out var parsed))
        {
            value = null;
            return false;
        }

        value = parsed;
        return true;
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
        OmitPageNumberOnFirstPage = page.OmitPageNumberOnFirstPage,
        Watermark = CloneWatermark(page.Watermark)
    };

    private static PageWatermark? CloneWatermark(PageWatermark? watermark)
    {
        if (watermark is null)
            return null;

        return new PageWatermark
        {
            Enabled = watermark.Enabled,
            Kind = watermark.Kind,
            Text = watermark.Text,
            FontFamily = watermark.FontFamily,
            FontSizePt = watermark.FontSizePt,
            Color = watermark.Color,
            Opacity = watermark.Opacity,
            RotationDegrees = watermark.RotationDegrees,
            Horizontal = watermark.Horizontal,
            Vertical = watermark.Vertical,
            Source = watermark.Source,
            WidthMm = watermark.WidthMm,
            HeightMm = watermark.HeightMm
        };
    }
}
