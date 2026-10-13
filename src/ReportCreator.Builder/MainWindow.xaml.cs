using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using ReportCreator.Compile;
using ReportCreator.Model;
using ReportCreator.Pdf;
using ModelText = ReportCreator.Model.TextBlock;

namespace ReportCreator.Builder;

public partial class MainWindow : Window
{
    private const string TinyPng = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private ReportDefinition _definition = new();
    private JsonElement _data = JsonSerializer.SerializeToElement(new { });
    private string? _path;
    private string? _previewFile;
    private Task<ReportPdfExporter>? _launch;
    private int _previewVersion;
    private bool _webReady;
    private bool _suppress;
    private bool _suppressData;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        _previewTimer.Tick += PreviewTick;
        LoadNew();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Preview.EnsureCoreWebView2Async();
            Preview.CoreWebView2.WebMessageReceived += WebMessageReceived;
            _webReady = true;
            SchedulePreview();
        }
        catch (Exception exception)
        {
            Status.Text = "WebView2 did not start. Install the WebView2 runtime. " + exception.Message;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewTimer.Stop();
        _previewVersion++;
        var launch = _launch;
        _launch = null;
        if (launch is not null)
            _ = DisposeExporter(launch);
        TryDelete(_previewFile);
        base.OnClosed(e);
    }

    private void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = JsonSerializer.Deserialize<SelectMessage>(e.TryGetWebMessageAsString(), ReportJson.Options);
            if (message?.Type == "select" && message.Id is not null)
                Select(message.Id);
        }
        catch (JsonException)
        {
        }
    }

    private void New_Click(object sender, RoutedEventArgs e) => LoadNew();

    private void Sample_Click(object sender, RoutedEventArgs e)
    {
        _path = null;
        _definition = ReportJson.Read(ReadEmbedded("invoice.definition.json"));
        SetData(ReportJson.ReadData(ReadEmbedded("invoice.data.json")));
        Normalize(_definition);
        Title = "See Attached — Invoice";
        RefreshList(_definition.Body.Blocks.FirstOrDefault()?.Id);
        Status.Text = "Invoice sample. Data is loaded for preview only.";
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Report (*.json;*.rcreport)|*.json;*.rcreport" };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            _definition = dialog.FileName.EndsWith(".rcreport", StringComparison.OrdinalIgnoreCase)
                ? ReadPackage(dialog.FileName)
                : ReportJson.Read(File.ReadAllText(dialog.FileName));
            Normalize(_definition);
            _path = dialog.FileName;
            Title = "See Attached — " + _definition.Name;
            RefreshList(null);
            Status.Text = "Opened " + dialog.FileName;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Open");
        }
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON data (*.json)|*.json" };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SetData(ReportJson.ReadData(File.ReadAllText(dialog.FileName)));
            SchedulePreview();
            Status.Text = "Preview data loaded.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Open data");
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_path is null)
            SaveAs_Click(sender, e);
        else
            Write(_path);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Report JSON (*.json)|*.json|Report package (*.rcreport)|*.rcreport",
            FileName = _definition.Name
        };
        if (dialog.ShowDialog() == true)
            Write(dialog.FileName);
    }

    private void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "HTML (*.html)|*.html", FileName = _definition.Name + ".html" };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, Compile(design: false));
            Status.Text = "Saved " + dialog.FileName;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Export HTML");
        }
    }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        var dialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = _definition.Name + ".pdf" };
        if (dialog.ShowDialog() != true)
            return;

        _busy = true;
        Status.Text = "Exporting PDF…";
        try
        {
            await using var exporter = await ReportPdfExporter.LaunchAsync();
            var bytes = await exporter.ExportAsync(_definition, _data);
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            Status.Text = "Saved " + dialog.FileName;
        }
        catch (Exception exception)
        {
            Status.Text = "PDF export failed.";
            MessageBox.Show(this, exception.GetBaseException().Message, "Export PDF");
        }
        finally
        {
            _busy = false;
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Page_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PageSetupWindow(_definition.Page) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _definition.Page = dialog.Page;
            SchedulePreview();
        }
    }

    private void FillData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetData(ReportBindings.MergeMissing(_data, ReportBindings.Sample(_definition)));
            SchedulePreview();
            Status.Text = "Missing fields inserted. Existing values were kept.";
        }
        catch (Exception exception)
        {
            Status.Text = exception.Message;
        }
    }

    private void DataBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressData)
            return;

        try
        {
            var data = ReportJson.ReadData(DataBox.Text);
            if (JsonSerializer.Serialize(data, ReportJson.Options) == JsonSerializer.Serialize(_data, ReportJson.Options))
                return;
            _data = data;
            SchedulePreview();
        }
        catch (JsonException exception)
        {
            Status.Text = exception.Message;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var kind = (AddKind.SelectedItem as ComboBoxItem)?.Content as string ?? "Text";
        var block = Create(kind);
        BandForAdd().Blocks.Add(block);
        RefreshList(block.Id);
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not BlockSite site)
            return;
        site.Parent.RemoveAt(site.Index);
        RefreshList(null);
    }

    private void BlockList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress)
            return;
        ShowProperties();
    }

    private void LoadNew()
    {
        _path = null;
        _definition = new ReportDefinition();
        SetData(JsonSerializer.SerializeToElement(new { }));
        _definition.Header.Blocks.Add(new ModelText { Id = NewId(), Text = "Report", Bold = true, FontSizePt = 22 });
        Title = "See Attached";
        RefreshList(_definition.Header.Blocks[0].Id);
        Status.Text = "New report.";
    }

    private void Move(int direction)
    {
        if (Selected() is not BlockSite site)
            return;
        var target = site.Index + direction;
        if (target < 0 || target >= site.Parent.Count)
            return;
        (site.Parent[site.Index], site.Parent[target]) = (site.Parent[target], site.Parent[site.Index]);
        RefreshList(site.Block.Id);
    }

    private void Select(string id)
    {
        if (BlockList.ItemsSource is not IEnumerable<BlockSite> sites)
            return;
        var site = sites.FirstOrDefault(item => item.Block.Id == id);
        if (site is null || Selected()?.Block.Id == id)
            return;
        _suppress = true;
        BlockList.SelectedItem = site;
        _suppress = false;
        ShowProperties();
    }

    private void RefreshList(string? selectId)
    {
        var sites = Flatten();
        _suppress = true;
        BlockList.ItemsSource = sites;
        BlockList.SelectedItem = selectId is null ? null : sites.FirstOrDefault(site => site.Block.Id == selectId);
        _suppress = false;
        ShowProperties();
        SchedulePreview();
    }

    private void ShowProperties()
    {
        Properties.Children.Clear();
        if (Selected()?.Block is not Block block)
            return;
        PropertyEditor.Fill(Properties, block, SchedulePreview, () =>
        {
            var id = block.Id;
            Dispatcher.BeginInvoke(() => RefreshList(id));
        });
    }

    private void SchedulePreview()
    {
        if (!_webReady)
            return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async void PreviewTick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        await RenderPreviewAsync();
    }

    private async Task RenderPreviewAsync()
    {
        if (!_webReady)
            return;

        var version = ++_previewVersion;
        if (!_busy)
            Status.Text = "Updating pages…";

        try
        {
            var exporter = await ExporterAsync();
            if (version != _previewVersion)
                return;

            var bytes = await exporter.ExportAsync(_definition, _data);
            if (version != _previewVersion)
                return;

            var path = Path.Combine(Path.GetTempPath(), "see-attached-" + Guid.NewGuid().ToString("N") + ".pdf");
            await File.WriteAllBytesAsync(path, bytes);
            if (version != _previewVersion)
            {
                TryDelete(path);
                return;
            }

            var previous = _previewFile;
            _previewFile = path;
            Preview.CoreWebView2.Navigate(new Uri(path).AbsoluteUri);
            TryDelete(previous);
            if (!_busy)
                Status.Text = _definition.Name;
        }
        catch (Exception exception)
        {
            if (version != _previewVersion)
                return;
            Status.Text = exception is ReportValidationException validation
                ? validation.Errors[0]
                : exception.GetBaseException().Message;
        }
    }

    private Task<ReportPdfExporter> ExporterAsync()
    {
        if (_launch is null || _launch.IsFaulted || _launch.IsCanceled)
            _launch = ReportPdfExporter.LaunchAsync();
        return _launch;
    }

    private static async Task DisposeExporter(Task<ReportPdfExporter> launch)
    {
        try
        {
            await (await launch).DisposeAsync();
        }
        catch (Exception)
        {
        }
    }

    private void SetData(JsonElement data)
    {
        _data = data;
        _suppressData = true;
        DataBox.Text = JsonSerializer.Serialize(data, ReportJson.Options);
        _suppressData = false;
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string Compile(bool design) =>
        ReportCompiler.Compile(_definition, _data, new CompileOptions
        {
            DesignMode = design,
            SelectedId = Selected()?.Block.Id,
            Culture = System.Globalization.CultureInfo.CurrentCulture
        });

    private void Write(string path)
    {
        var errors = ReportValidator.Validate(_definition);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Save");
            return;
        }

        var json = ReportJson.Write(_definition);
        if (path.EndsWith(".rcreport", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
                File.Delete(path);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry("definition.json").Open());
            writer.Write(json);
        }
        else
        {
            File.WriteAllText(path, json);
        }

        _path = path;
        Title = "See Attached — " + _definition.Name;
        Status.Text = "Saved " + path;
    }

    private Band BandForAdd() => AddBand.SelectedIndex switch
    {
        0 => _definition.Header,
        2 => _definition.Footer,
        3 => _definition.PageHeader,
        4 => _definition.PageFooter,
        _ => _definition.Body
    };

    private BlockSite? Selected() => BlockList.SelectedItem as BlockSite;

    private List<BlockSite> Flatten()
    {
        var sites = new List<BlockSite>();
        Walk(_definition.PageHeader.Blocks, "Page header", 0, sites);
        Walk(_definition.Header.Blocks, "Header", 0, sites);
        Walk(_definition.Body.Blocks, "Body", 0, sites);
        Walk(_definition.Footer.Blocks, "Footer", 0, sites);
        Walk(_definition.PageFooter.Blocks, "Page footer", 0, sites);
        return sites;
    }

    private static void Walk(List<Block> list, string band, int depth, List<BlockSite> sites)
    {
        for (var index = 0; index < list.Count; index++)
        {
            var block = list[index];
            sites.Add(new BlockSite(list, index, band, depth, block));
            if (block is not ColumnsBlock columns)
                continue;
            for (var column = 0; column < columns.Columns.Count; column++)
                Walk(columns.Columns[column].Blocks, band, depth + 1, sites);
        }
    }

    private static Block Create(string kind) => kind switch
    {
        "Field" => new FieldBlock { Id = NewId(), Path = "name" },
        "Image" => new ImageBlock { Id = NewId(), Source = TinyPng, Alt = "", WidthMm = 40 },
        "Spacer" => new SpacerBlock { Id = NewId(), HeightMm = 6 },
        "Columns" => new ColumnsBlock { Id = NewId(), Columns = [new Column(), new Column()] },
        "Table" => new TableBlock
        {
            Id = NewId(),
            DataPath = "lines",
            Columns = [new TableColumn { Header = "Description", Path = "description" }]
        },
        _ => new ModelText { Id = NewId(), Text = "Text" }
    };

    private static string NewId() => "b" + Guid.NewGuid().ToString("N")[..8];

    private static void Normalize(ReportDefinition definition)
    {
        definition.Page ??= new PageSetup();
        definition.Page.Margin ??= new Margin();
        definition.Header ??= new Band();
        definition.Body ??= new Band();
        definition.Footer ??= new Band();
        definition.PageHeader ??= new Band();
        definition.PageFooter ??= new Band();
        definition.Header.Blocks ??= [];
        definition.Body.Blocks ??= [];
        definition.Footer.Blocks ??= [];
        definition.PageHeader.Blocks ??= [];
        definition.PageFooter.Blocks ??= [];
    }

    private static ReportDefinition ReadPackage(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("definition.json")
            ?? throw new InvalidDataException("The package has no definition.json.");
        using var reader = new StreamReader(entry.Open());
        return ReportJson.Read(reader.ReadToEnd());
    }

    private static string ReadEmbedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing sample " + name + ".");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record BlockSite(List<Block> Parent, int Index, string Band, int Depth, Block Block)
    {
        public string Label => new string(' ', Depth * 2) + Band + "  " + PropertyEditor.Kind(Block) + "  " + Block.Id;
    }

    private sealed record SelectMessage(string? Type, string? Id);
}
