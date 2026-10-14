using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReportCreator.Model;

public static class DataEdit
{
    public static DataField? Find(DataDocument document, string path) =>
        Find(document.Fields, path);

    public static DataField? Find(IReadOnlyList<DataField>? fields, string? path)
    {
        if (fields is null || string.IsNullOrWhiteSpace(path))
            return null;

        DataField? current = null;
        var list = fields;
        foreach (var part in path.Split('.'))
        {
            current = list.FirstOrDefault(field => field.Name == part);
            if (current is null)
                return null;
            list = current.Fields ?? [];
        }

        return current;
    }

    public static bool Ensure(DataDocument document, string path, DataFieldType type)
    {
        if (!CanWalk(path))
            return false;

        document.Fields ??= [];
        var root = Mutable(document);
        var ok = EnsureObject(document.Fields, root, path.Split('.'), type);
        Commit(document, root);
        return ok;
    }

    public static bool EnsureColumn(DataDocument document, string arrayPath, string columnPath, DataFieldType type)
    {
        if (!Ensure(document, arrayPath, DataFieldType.Array) || !CanWalk(columnPath))
            return false;

        var arrayField = Find(document, arrayPath);
        if (arrayField is null || arrayField.Type != DataFieldType.Array)
            return false;

        arrayField.Fields ??= [];
        if (!EnsureObject(arrayField.Fields, new JsonObject(), columnPath.Split('.'), type))
            return false;

        var root = Mutable(document);
        var arrays = ArraysAt(root, document, arrayPath, create: false);
        if (arrays.Count == 0)
            return true;

        foreach (var rows in arrays)
        {
            foreach (var row in rows)
            {
                if (row is JsonObject rowObject)
                    FillMissing(arrayField.Fields, rowObject, columnPath.Split('.'));
            }
        }

        Commit(document, root);
        return true;
    }

    public static DataField Add(DataDocument document, string? parentPath, string name, DataFieldType type)
    {
        if (!ParameterRules.IsName(name))
            throw new ArgumentException("A field name must be letters, digits, and underscores.");

        var root = Mutable(document);
        List<DataField> siblings;
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            document.Fields ??= [];
            siblings = document.Fields;
        }
        else
        {
            var parent = Find(document, parentPath) ?? throw new ArgumentException($"Field '{parentPath}' was not found.");
            if (parent.Type is not (DataFieldType.Object or DataFieldType.Array))
                throw new ArgumentException("Add a field under an object or an array.");
            parent.Fields ??= [];
            siblings = parent.Fields;
        }

        if (siblings.Any(field => field.Name == name))
            throw new ArgumentException($"'{name}' is already a field.");

        var field = new DataField { Name = name, Type = type };
        siblings.Add(field);
        var created = DefaultNode(type);
        foreach (var holder in Enter(root, document, parentPath, create: true))
            holder[name] = created.DeepClone();

        Commit(document, root);
        return field;
    }

    public static void Rename(DataDocument document, string path, string name)
    {
        if (!ParameterRules.IsName(name))
            throw new ArgumentException("A field name must be letters, digits, and underscores.");

        var parts = Split(path);
        var siblings = Siblings(document, parts.Parent);
        var field = siblings.FirstOrDefault(item => item.Name == parts.Name) ?? throw new ArgumentException($"Field '{path}' was not found.");
        if (siblings.Any(item => item.Name == name && !ReferenceEquals(item, field)))
            throw new ArgumentException($"'{name}' is already a field.");

        var root = Mutable(document);
        RenameNode(document, root, path, name);
        field.Name = name;
        Commit(document, root);
    }

    public static void Remove(DataDocument document, string path)
    {
        var parts = Split(path);
        var siblings = Siblings(document, parts.Parent);
        var field = siblings.FirstOrDefault(item => item.Name == parts.Name);
        if (field is null)
            return;

        var root = Mutable(document);
        RemoveNode(document, root, path);
        siblings.Remove(field);
        Commit(document, root);
    }

    public static void SetType(DataDocument document, string path, DataFieldType type)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentException("That field type is not supported.");

        var field = Find(document, path) ?? throw new ArgumentException($"Field '{path}' was not found.");
        if (field.Type == type)
            return;

        var root = Mutable(document);
        Convert(root, document, path, type);
        if (type is DataFieldType.String or DataFieldType.Number or DataFieldType.Date or DataFieldType.Bool)
            field.Fields = [];
        field.Type = type;
        Commit(document, root);
    }

    public static void SetValue(DataDocument document, string path, string text)
    {
        var field = Find(document, path) ?? throw new ArgumentException($"Field '{path}' was not found.");
        if (field.Type is DataFieldType.Object or DataFieldType.Array)
            throw new ArgumentException("Set a value on a string, number, date, or bool.");
        if (TryRowPath(document, path, out _, out _))
            throw new ArgumentException("Change a row value on the row, not on the column.");

        var root = Mutable(document);
        var parts = path.Split('.');
        var parent = ObjectAt(root, parts[..^1], create: true);
        parent[parts[^1]] = ParseNode(field.Type, text);
        Commit(document, root);
    }

    public static void AddRow(DataDocument document, string path)
    {
        var field = Find(document, path) ?? throw new ArgumentException($"Field '{path}' was not found.");
        if (field.Type != DataFieldType.Array)
            throw new ArgumentException($"Field '{path}' is not an array.");

        var root = Mutable(document);
        var arrays = ArraysAt(root, document, path, create: true);
        if (arrays.Count == 0)
            throw new ArgumentException("That row was not found.");

        foreach (var rows in arrays)
        {
            var row = new JsonObject();
            WriteDefaults(field.Fields, row);
            rows.Add(row);
        }

        Commit(document, root);
    }

    public static void RemoveRow(DataDocument document, string path, int index)
    {
        var field = Find(document, path) ?? throw new ArgumentException($"Field '{path}' was not found.");
        if (field.Type != DataFieldType.Array)
            throw new ArgumentException($"Field '{path}' is not an array.");

        var root = Mutable(document);
        var removed = false;
        foreach (var rows in ArraysAt(root, document, path, create: false))
        {
            if (index < 0 || index >= rows.Count)
                continue;
            rows.RemoveAt(index);
            removed = true;
        }

        if (!removed)
            throw new ArgumentException("That row was not found.");
        Commit(document, root);
    }

    public static void SetCell(DataDocument document, string arrayPath, int index, string columnPath, string text)
    {
        var arrayField = Find(document, arrayPath) ?? throw new ArgumentException($"Field '{arrayPath}' was not found.");
        if (arrayField.Type != DataFieldType.Array)
            throw new ArgumentException($"Field '{arrayPath}' is not an array.");
        var column = Find(arrayField.Fields, columnPath) ?? throw new ArgumentException($"Field '{columnPath}' was not found.");

        var root = Mutable(document);
        var sample = ParseNode(column.Type, text);
        var wrote = false;
        foreach (var rows in ArraysAt(root, document, arrayPath, create: false))
        {
            if (index < 0 || index >= rows.Count || rows[index] is not JsonObject row)
                continue;
            Assign(row, columnPath.Split('.'), sample.DeepClone());
            wrote = true;
        }

        if (!wrote)
            throw new ArgumentException("That row was not found.");
        Commit(document, root);
    }

    private static bool EnsureObject(List<DataField> fields, JsonObject obj, string[] parts, DataFieldType leaf)
    {
        var name = parts[0];
        if (!ParameterRules.IsName(name))
            return false;

        var field = fields.FirstOrDefault(item => item.Name == name);
        if (parts.Length == 1)
        {
            if (field is null)
            {
                fields.Add(new DataField { Name = name, Type = leaf });
                if (leaf is DataFieldType.Object or DataFieldType.Array
                    && obj[name] is not JsonObject
                    && obj[name] is not JsonArray)
                    obj[name] = DefaultNode(leaf);
                return true;
            }

            return field.Type == leaf;
        }

        if (field is null)
        {
            field = new DataField { Name = name, Type = DataFieldType.Object };
            fields.Add(field);
            obj[name] = new JsonObject();
        }

        if (field.Type != DataFieldType.Object)
            return false;

        field.Fields ??= [];
        if (obj[name] is not JsonObject child)
        {
            child = [];
            obj[name] = child;
        }

        return EnsureObject(field.Fields, child, parts[1..], leaf);
    }

    private static void FillMissing(List<DataField> fields, JsonObject obj, string[] parts)
    {
        var field = fields.FirstOrDefault(item => item.Name == parts[0]);
        if (field is null)
            return;
        if (parts.Length == 1)
        {
            if (!obj.ContainsKey(field.Name))
                obj[field.Name] = DefaultNode(field.Type);
            return;
        }

        if (obj[field.Name] is not JsonObject child)
        {
            child = [];
            obj[field.Name] = child;
        }

        FillMissing(field.Fields ?? [], child, parts[1..]);
    }

    private static void WriteDefaults(List<DataField>? fields, JsonObject obj)
    {
        foreach (var field in fields ?? [])
        {
            if (field.Type == DataFieldType.Object)
            {
                var child = new JsonObject();
                WriteDefaults(field.Fields, child);
                obj[field.Name] = child;
            }
            else
            {
                obj[field.Name] = DefaultNode(field.Type);
            }
        }
    }

    private static void Convert(JsonObject root, DataDocument document, string path, DataFieldType type)
    {
        if (TryRowPath(document, path, out var arrayPath, out var inside))
        {
            if (NodeAt(root, arrayPath.Split('.')) is not JsonArray rows)
                return;
            foreach (var row in rows)
            {
                if (row is not JsonObject rowObject)
                    continue;
                var parts = inside.Split('.');
                var parent = ObjectAt(rowObject, parts[..^1], create: true);
                parent[parts[^1]] = parent[parts[^1]] is { } current ? ConvertNode(current, type) : DefaultNode(type);
            }

            return;
        }

        var pathParts = path.Split('.');
        var holder = ObjectAt(root, pathParts[..^1], create: true);
        holder[pathParts[^1]] = holder[pathParts[^1]] is { } existing ? ConvertNode(existing, type) : DefaultNode(type);
    }

    private static JsonNode ConvertNode(JsonNode node, DataFieldType type)
    {
        try
        {
            return ParseNode(type, AsText(node));
        }
        catch (ArgumentException)
        {
            return DefaultNode(type);
        }
    }

    private static string AsText(JsonNode node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return text ?? "";
        if (node is JsonValue)
            return node.ToJsonString();
        return node.ToJsonString();
    }

    private static void RenameNode(DataDocument document, JsonObject root, string path, string name)
    {
        var parts = Split(path);
        if (TryRowPath(document, path, out var arrayPath, out var inside))
        {
            if (NodeAt(root, arrayPath.Split('.')) is not JsonArray rows)
                return;
            var leaf = inside.Split('.')[^1];
            foreach (var row in rows)
            {
                if (row is not JsonObject rowObject)
                    continue;
                var parent = ObjectAt(rowObject, inside.Split('.')[..^1], create: false);
                if (!parent.ContainsKey(leaf))
                    continue;
                parent[name] = parent[leaf];
                parent.Remove(leaf);
            }

            return;
        }

        var holder = ObjectAt(root, path.Split('.')[..^1], create: false);
        if (!holder.ContainsKey(parts.Name))
            return;
        holder[name] = holder[parts.Name];
        holder.Remove(parts.Name);
    }

    private static void RemoveNode(DataDocument document, JsonObject root, string path)
    {
        var parts = Split(path);
        if (TryRowPath(document, path, out var arrayPath, out var inside))
        {
            if (NodeAt(root, arrayPath.Split('.')) is not JsonArray rows)
                return;
            var leaf = inside.Split('.')[^1];
            foreach (var row in rows)
            {
                if (row is not JsonObject rowObject)
                    continue;
                ObjectAt(rowObject, inside.Split('.')[..^1], create: false).Remove(leaf);
            }

            return;
        }

        ObjectAt(root, path.Split('.')[..^1], create: false).Remove(parts.Name);
    }

    private static bool TryRowPath(DataDocument document, string path, out string arrayPath, out string inside)
    {
        arrayPath = "";
        inside = "";
        var parts = path.Split('.');
        var walked = new List<string>();
        IReadOnlyList<DataField> fields = document.Fields ?? [];
        for (var index = 0; index < parts.Length; index++)
        {
            var field = fields.FirstOrDefault(item => item.Name == parts[index]);
            if (field is null)
                return false;
            walked.Add(parts[index]);
            if (field.Type == DataFieldType.Array && index < parts.Length - 1)
            {
                arrayPath = string.Join('.', walked);
                inside = string.Join('.', parts[(index + 1)..]);
                return true;
            }

            fields = field.Fields ?? [];
        }

        return false;
    }

    private static List<DataField> Siblings(DataDocument document, string? parentPath)
    {
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            document.Fields ??= [];
            return document.Fields;
        }

        var parent = Find(document, parentPath) ?? throw new ArgumentException($"Field '{parentPath}' was not found.");
        parent.Fields ??= [];
        return parent.Fields;
    }

    private static (string? Parent, string Name) Split(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A field path is required.");
        var parts = path.Split('.');
        if (!CanWalk(path))
            throw new ArgumentException($"Field '{path}' needs a name made of letters, digits, and underscores.");
        return parts.Length == 1 ? (null, parts[0]) : (string.Join('.', parts[..^1]), parts[^1]);
    }

    private static bool CanWalk(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.Split('.').All(ParameterRules.IsName);

    private static void Assign(JsonObject root, string[] parts, JsonNode node)
    {
        var current = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (current[parts[index]] is not JsonObject next)
            {
                next = [];
                current[parts[index]] = next;
            }

            current = next;
        }

        current[parts[^1]] = node;
    }

    private static JsonNode ParseNode(DataFieldType type, string? text)
    {
        text ??= "";
        switch (type)
        {
            case DataFieldType.String:
                return JsonValue.Create(text)!;
            case DataFieldType.Number:
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    throw new ArgumentException("Enter a number.");
                return JsonValue.Create(number)!;
            case DataFieldType.Date:
                if (string.IsNullOrWhiteSpace(text))
                    return JsonValue.Create("")!;
                if (!DataJson.IsDate(text.Trim()))
                    throw new ArgumentException("Enter a date as yyyy-MM-dd.");
                return JsonValue.Create(text.Trim())!;
            case DataFieldType.Bool:
                if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
                    return JsonValue.Create(true)!;
                if (text.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return JsonValue.Create(false)!;
                throw new ArgumentException("Enter true or false.");
            case DataFieldType.Object:
                return ParseContainer(text, JsonValueKind.Object, "Enter a JSON object.");
            case DataFieldType.Array:
                return ParseContainer(text, JsonValueKind.Array, "Enter a JSON array.");
            default:
                throw new ArgumentException("That field type is not supported.");
        }
    }

    private static JsonNode ParseContainer(string text, JsonValueKind kind, string message)
    {
        try
        {
            var node = JsonNode.Parse(string.IsNullOrWhiteSpace(text) ? (kind == JsonValueKind.Array ? "[]" : "{}") : text)
                ?? throw new ArgumentException(message);
            if (node is JsonObject && kind == JsonValueKind.Object)
                return node;
            if (node is JsonArray && kind == JsonValueKind.Array)
                return node;
        }
        catch (JsonException)
        {
            throw new ArgumentException(message);
        }

        throw new ArgumentException(message);
    }

    private static JsonNode DefaultNode(DataFieldType type) => type switch
    {
        DataFieldType.Number => JsonValue.Create(0m)!,
        DataFieldType.Bool => JsonValue.Create(false)!,
        DataFieldType.Object => new JsonObject(),
        DataFieldType.Array => new JsonArray(),
        _ => JsonValue.Create("")!
    };

    private static JsonObject Mutable(DataDocument document)
    {
        if (document.Values.ValueKind != JsonValueKind.Object)
            return [];
        return JsonNode.Parse(document.Values.GetRawText()) as JsonObject ?? [];
    }

    private static void Commit(DataDocument document, JsonObject root) =>
        document.Values = JsonSerializer.SerializeToElement(root);

    private static JsonNode? NodeAt(JsonObject root, string[] parts)
    {
        JsonNode? current = root;
        foreach (var part in parts)
        {
            if (current is not JsonObject obj)
                return null;
            current = obj[part];
        }

        return current;
    }

    private static JsonObject ObjectAt(JsonObject root, string[] parts, bool create)
    {
        var current = root;
        foreach (var part in parts)
        {
            if (current[part] is not JsonObject next)
            {
                if (!create)
                    return [];
                next = [];
                current[part] = next;
            }

            current = next;
        }

        return current;
    }

    private static List<JsonObject> Enter(JsonObject root, DataDocument document, string? path, bool create)
    {
        var current = new List<JsonObject> { root };
        if (string.IsNullOrWhiteSpace(path))
            return current;

        var walked = "";
        foreach (var part in path.Split('.'))
        {
            walked = walked.Length == 0 ? part : walked + "." + part;
            var asArray = Find(document, walked)?.Type == DataFieldType.Array;
            var next = new List<JsonObject>();
            foreach (var node in current)
            {
                var child = node[part];
                if (asArray || child is JsonArray)
                {
                    if (child is not JsonArray array)
                    {
                        if (!create)
                            continue;
                        array = [];
                        node[part] = array;
                    }

                    foreach (var row in array)
                    {
                        if (row is JsonObject rowObject)
                            next.Add(rowObject);
                    }
                }
                else
                {
                    if (child is not JsonObject obj)
                    {
                        if (!create)
                            continue;
                        obj = [];
                        node[part] = obj;
                    }

                    next.Add(obj);
                }
            }

            current = next;
        }

        return current;
    }

    private static List<JsonArray> ArraysAt(JsonObject root, DataDocument document, string path, bool create)
    {
        var parts = path.Split('.');
        var owners = parts.Length == 1
            ? new List<JsonObject> { root }
            : Enter(root, document, string.Join('.', parts[..^1]), create);
        var name = parts[^1];
        var arrays = new List<JsonArray>();
        foreach (var owner in owners)
        {
            if (owner[name] is not JsonArray array)
            {
                if (!create)
                    continue;
                array = [];
                owner[name] = array;
            }

            arrays.Add(array);
        }

        return arrays;
    }
}
