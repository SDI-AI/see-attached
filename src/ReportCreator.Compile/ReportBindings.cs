using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ReportCreator.Model;

namespace ReportCreator.Compile;

public enum ReportBindingKind
{
    Value,
    Table,
    Column,
    Group,
    Condition
}

public sealed record ReportBinding(string Path, ReportBindingKind Kind, string? Format = null, string? TablePath = null)
{
    public string Display => TablePath is null ? Path : TablePath + "[]." + Path;
}

public static partial class ReportBindings
{
    public static IReadOnlyList<ReportBinding> List(ReportDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var found = new List<ReportBinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(ReportBinding binding)
        {
            if (seen.Add(binding.Kind + ":" + binding.TablePath + ":" + binding.Path))
                found.Add(binding);
        }

        foreach (var band in Bands(definition))
        {
            if (band.Blocks is null)
                continue;
            foreach (var block in band.Blocks)
                Walk(block, Add, itemPath: null, parentPath: null);
        }

        return found;
    }

    public static JsonElement Sample(ReportDefinition definition)
    {
        var root = new Node();
        foreach (var binding in List(definition))
        {
            if (PrintPaths.IsPrintField(binding.Path) || PrintPaths.TryParameterName(binding.Path, out _))
                continue;

            switch (binding.Kind)
            {
                case ReportBindingKind.Table:
                    root.Array(binding.Path);
                    break;
                case ReportBindingKind.Column:
                case ReportBindingKind.Group:
                    root.Array(binding.TablePath!);
                    root.Value(binding.TablePath + "." + binding.Path, SampleValue(binding.Kind == ReportBindingKind.Group ? null : binding.Format));
                    break;
                default:
                    root.Value(binding.Path, SampleValue(binding.Format));
                    break;
            }
        }

        return root.ToElement();
    }

    public static void Include(DataDocument document, ReportDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(definition);
        foreach (var binding in List(definition))
        {
            if (PrintPaths.IsPrintField(binding.Path) || PrintPaths.TryParameterName(binding.Path, out _))
                continue;

            var type = binding.Kind == ReportBindingKind.Group ? DataFieldType.String : TypeFor(binding.Format);
            if (binding.Kind == ReportBindingKind.Table)
                EnsureChain(document, binding.Path, DataFieldType.Array);
            else if (binding.Kind is ReportBindingKind.Column or ReportBindingKind.Group)
                EnsureChain(document, binding.TablePath + "." + binding.Path, type);
            else
                EnsureChain(document, binding.Path, type);
        }

        document.Values = MergeMissing(document.Values, Sample(definition));
    }

    private static DataFieldType TypeFor(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return DataFieldType.String;
        if (format.Contains('y') || format.Contains('M') || format.Contains('d'))
            return DataFieldType.Date;
        try
        {
            _ = 1.5m.ToString(format, CultureInfo.InvariantCulture);
            return DataFieldType.Number;
        }
        catch (FormatException)
        {
            return DataFieldType.String;
        }
    }

    public static JsonElement MergeMissing(JsonElement existing, JsonElement sample)
    {
        if (existing.ValueKind != JsonValueKind.Object)
            return sample.Clone();

        var target = JsonNode.Parse(existing.GetRawText()) ?? new JsonObject();
        var incoming = JsonNode.Parse(sample.GetRawText()) ?? new JsonObject();
        Merge(target, incoming);
        return JsonSerializer.SerializeToElement(target);
    }

    private static void Walk(Block block, Action<ReportBinding> add, string? itemPath, string? parentPath)
    {
        if (block.When is { Path.Length: > 0 })
            AddScoped(add, block.When.Path, ReportBindingKind.Condition, null, itemPath, parentPath);

        switch (block)
        {
            case TextBlock text:
                foreach (Match match in Token().Matches(text.Text))
                    AddScoped(add, match.Groups[1].Value, ReportBindingKind.Value, match.Groups[2].Success ? match.Groups[2].Value : null, itemPath, parentPath);
                if (text.Runs is not null)
                {
                    foreach (var run in text.Runs)
                    {
                        if (!string.IsNullOrWhiteSpace(run.Path))
                            AddScoped(add, run.Path, ReportBindingKind.Value, run.Format, itemPath, parentPath);
                        foreach (Match match in Token().Matches(run.Text ?? ""))
                            AddScoped(add, match.Groups[1].Value, ReportBindingKind.Value, match.Groups[2].Success ? match.Groups[2].Value : null, itemPath, parentPath);
                    }
                }
                break;
            case ImageBlock image when !string.IsNullOrWhiteSpace(image.SourcePath):
                AddScoped(add, image.SourcePath, ReportBindingKind.Value, null, itemPath, parentPath);
                break;
            case FieldBlock field when field.Path.Length > 0:
                AddScoped(add, field.Path, ReportBindingKind.Value, field.Format, itemPath, parentPath);
                break;
            case ColumnsBlock columns:
                foreach (var column in columns.Columns)
                {
                    foreach (var child in column.Blocks)
                        Walk(child, add, itemPath, parentPath);
                }
                break;
            case PanelBlock panel:
                foreach (var child in panel.Blocks ?? [])
                    Walk(child, add, itemPath, parentPath);
                break;
            case RepeatBlock repeat when repeat.DataPath.Length > 0:
                var repeatPath = Absolute(repeat.DataPath, itemPath, parentPath);
                add(new ReportBinding(repeatPath, ReportBindingKind.Table));
                foreach (var child in repeat.Blocks ?? [])
                    Walk(child, add, repeatPath, itemPath);
                break;
            case TableBlock table when table.DataPath.Length > 0:
                var tablePath = Absolute(table.DataPath, itemPath, parentPath);
                add(new ReportBinding(tablePath, ReportBindingKind.Table));
                foreach (var column in table.Columns)
                {
                    if (itemPath is not null && DataScope.TryScopePrefix(column.Path, out _, out _))
                        AddScoped(add, column.Path, ReportBindingKind.Column, column.Format, itemPath, parentPath);
                    else
                        add(new ReportBinding(column.Path, ReportBindingKind.Column, column.Format, tablePath));
                }
                if (!string.IsNullOrWhiteSpace(table.GroupBy))
                    add(new ReportBinding(table.GroupBy, ReportBindingKind.Group, null, tablePath));
                if (!string.IsNullOrWhiteSpace(table.GroupKey))
                    add(new ReportBinding(table.GroupKey, ReportBindingKind.Group, null, tablePath));
                break;
        }
    }

    private static void AddScoped(Action<ReportBinding> add, string path, ReportBindingKind kind, string? format, string? itemPath, string? parentPath)
    {
        if (itemPath is not null && DataScope.TryScopePrefix(path, out var head, out var rest) && rest is not null)
        {
            if (head == "item")
            {
                add(new ReportBinding(rest, kind is ReportBindingKind.Value or ReportBindingKind.Condition ? ReportBindingKind.Column : kind, format, itemPath));
                return;
            }

            if (head == "parent" && parentPath is not null)
            {
                add(new ReportBinding(rest, ReportBindingKind.Column, format, parentPath));
                return;
            }

            if (head == "root")
            {
                add(new ReportBinding(rest, ReportBindingKind.Value, format));
                return;
            }
        }

        if (itemPath is not null && kind == ReportBindingKind.Column)
        {
            add(new ReportBinding(path, ReportBindingKind.Column, format, itemPath));
            return;
        }

        add(new ReportBinding(Absolute(path, itemPath, parentPath), kind, format));
    }

    private static string Absolute(string path, string? itemPath, string? parentPath)
    {
        if (itemPath is null || !DataScope.TryScopePrefix(path, out var head, out var rest))
            return path;
        if (head == "root")
            return rest ?? path;
        if (head == "item")
            return Join(itemPath, rest);
        if (head == "parent")
            return parentPath is null ? rest ?? path : Join(parentPath, rest);
        return path;
    }

    private static string Join(string left, string? right) =>
        string.IsNullOrEmpty(right) ? left : left + "." + right;

    private static void EnsureChain(DataDocument document, string? path, DataFieldType leaf)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var parts = path.Split('.');
        if (parts.Any(part => !ParameterRules.IsName(part)))
            return;

        document.Fields ??= [];
        var fields = document.Fields;
        for (var index = 0; index < parts.Length; index++)
        {
            var name = parts[index];
            var last = index == parts.Length - 1;
            var field = fields.FirstOrDefault(item => item.Name == name);
            if (field is null)
            {
                field = new DataField
                {
                    Name = name,
                    Type = last ? leaf : DataFieldType.Object
                };
                if (field.Type is DataFieldType.Object or DataFieldType.Array)
                    field.Fields = [];
                fields.Add(field);
            }

            if (!last && field.Type is DataFieldType.Object or DataFieldType.Array)
            {
                field.Fields ??= [];
                fields = field.Fields;
            }
        }
    }

    private static IEnumerable<Band> Bands(ReportDefinition definition)
    {
        if (definition.PageHeader is not null)
            yield return definition.PageHeader;
        if (definition.Header is not null)
            yield return definition.Header;
        if (definition.Body is not null)
            yield return definition.Body;
        if (definition.Footer is not null)
            yield return definition.Footer;
        if (definition.PageFooter is not null)
            yield return definition.PageFooter;
    }

    private static object SampleValue(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return "";
        if (format.Contains('y') || format.Contains('M') || format.Contains('d'))
            return "2026-01-01";
        try
        {
            _ = 1.5m.ToString(format, CultureInfo.InvariantCulture);
            return 0;
        }
        catch (FormatException)
        {
            return "";
        }
    }

    private static void Merge(JsonNode target, JsonNode incoming)
    {
        if (target is not JsonObject targetObject || incoming is not JsonObject incomingObject)
            return;

        foreach (var pair in incomingObject)
        {
            if (!targetObject.TryGetPropertyValue(pair.Key, out var current) || current is null)
            {
                targetObject[pair.Key] = pair.Value?.DeepClone();
                continue;
            }

            if (current is JsonObject && pair.Value is JsonObject)
            {
                Merge(current, pair.Value);
                continue;
            }

            if (current is JsonArray currentRows && pair.Value is JsonArray sampleRows
                && currentRows.Count > 0 && sampleRows.Count > 0
                && currentRows[0] is JsonObject row && sampleRows[0] is JsonObject sampleRow)
                Merge(row, sampleRow);
            else if (current is JsonArray empty && empty.Count == 0 && pair.Value is JsonArray filled && filled.Count > 0)
                targetObject[pair.Key] = filled.DeepClone();
        }
    }

    [GeneratedRegex("\\{([A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*)(?::([^{}]+))?\\}")]
    private static partial Regex Token();

    private sealed class Node
    {
        private readonly Dictionary<string, object?> _values = [];

        public void Value(string path, object? value) => Place(_values, path.Split('.'), value, array: false);

        public void Array(string path) => Place(_values, path.Split('.'), new List<Dictionary<string, object?>> { new Dictionary<string, object?>() }, array: true);

        private static void Place(Dictionary<string, object?> node, string[] parts, object? value, bool array)
        {
            if (parts.Length == 1)
            {
                if (!node.ContainsKey(parts[0]))
                    node[parts[0]] = value;
                return;
            }

            if (node.TryGetValue(parts[0], out var existing) && existing is List<Dictionary<string, object?>> rows)
            {
                if (rows.Count == 0)
                    rows.Add(new Dictionary<string, object?>());
                Place(rows[0], parts[1..], value, array);
                return;
            }

            if (node.GetValueOrDefault(parts[0]) is not Dictionary<string, object?> child)
            {
                child = [];
                node[parts[0]] = child;
            }

            Place(child, parts[1..], value, array);
        }

        public JsonElement ToElement() => JsonSerializer.SerializeToElement(_values);
    }
}
