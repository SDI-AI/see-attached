using System.Globalization;
using ReportCreator.Compile;
using ReportCreator.Model;

var root = FindRoot();
WriteHtml(root, "invoice");
WriteHtml(root, "orders");

static void WriteHtml(string root, string name)
{
    var definition = ReportJson.Read(File.ReadAllText(Path.Combine(root, "fixtures", name, "definition.json")));
    var data = ReportJson.ReadData(File.ReadAllText(Path.Combine(root, "fixtures", name, "data.json")));
    var html = ReportCompiler.Compile(definition, data, new CompileOptions
    {
        Culture = CultureInfo.GetCultureInfo("en-US"),
        Clock = new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero)
    });
    var output = Path.Combine(root, "samples", "viewer-next", "public", name + ".html");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, html);
    Console.WriteLine(output);
}

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ReportCreator.slnx")))
            return dir.FullName;
        dir = dir.Parent;
    }

    throw new InvalidOperationException("Could not find the repository root.");
}
