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
                Walk(block, Add);
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
                    root.RowValue(binding.TablePath!, binding.Path, SampleValue(binding.Format));
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

            var type = TypeFor(binding.Format);
            if (binding.Kind == ReportBindingKind.Table)
                DataEdit.Ensure(document, binding.Path, DataFieldType.Array);
            else if (binding.Kind is ReportBindingKind.Column or ReportBindingKind.Group)
                DataEdit.EnsureColumn(document, binding.TablePath ?? "", binding.Path, binding.Kind == ReportBindingKind.Group ? DataFieldType.String : type);
            else
                DataEdit.Ensure(document, binding.Path, type);
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

    private static void Walk(Block block, Action<ReportBinding> add)
    {
        if (block.When is { Path.Length: > 0 })
            add(new ReportBinding(block.When.Path, ReportBindingKind.Condition));

        switch (block)
        {
            case TextBlock text:
                foreach (Match match in Token().Matches(text.Text))
                    add(new ReportBinding(match.Groups[1].Value, ReportBindingKind.Value, match.Groups[2].Success ? match.Groups[2].Value : null));
                break;
            case FieldBlock field when field.Path.Length > 0:
                add(new ReportBinding(field.Path, ReportBindingKind.Value, field.Format));
                break;
            case ColumnsBlock columns:
                foreach (var column in columns.Columns)
                {
                    foreach (var child in column.Blocks)
                        Walk(child, add);
                }
                break;
            case TableBlock table when table.DataPath.Length > 0:
                add(new ReportBinding(table.DataPath, ReportBindingKind.Table));
                foreach (var column in table.Columns)
                    add(new ReportBinding(column.Path, ReportBindingKind.Column, column.Format, table.DataPath));
                if (!string.IsNullOrWhiteSpace(table.GroupBy))
                    add(new ReportBinding(table.GroupBy, ReportBindingKind.Group, null, table.DataPath));
                break;
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

        public void Value(string path, object? value) => Set(_values, path.Split('.'), value);

        public void Array(string path)
        {
            var parts = path.Split('.');
            var parent = Ensure(_values, parts[..^1]);
            if (!parent.ContainsKey(parts[^1]))
                parent[parts[^1]] = new List<Dictionary<string, object?>> { new Dictionary<string, object?>() };
        }

        public void RowValue(string tablePath, string columnPath, object? value)
        {
            Array(tablePath);
            var parts = tablePath.Split('.');
            var parent = Ensure(_values, parts[..^1]);
            var rows = (List<Dictionary<string, object?>>)parent[parts[^1]]!;
            Set(rows[0], columnPath.Split('.'), value);
        }

        public JsonElement ToElement() => JsonSerializer.SerializeToElement(_values);

        private static Dictionary<string, object?> Ensure(Dictionary<string, object?> node, string[] parts)
        {
            foreach (var part in parts)
            {
                if (!node.TryGetValue(part, out var next) || next is not Dictionary<string, object?> child)
                {
                    child = [];
                    node[part] = child;
                }

                node = child;
            }

            return node;
        }

        private static void Set(Dictionary<string, object?> node, string[] parts, object? value)
        {
            var parent = Ensure(node, parts[..^1]);
            parent[parts[^1]] = value;
        }
    }
}
