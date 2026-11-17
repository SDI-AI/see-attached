using System.Text.Json;

namespace ReportCreator.Compile;

internal sealed class DataScope
{
    public required JsonElement Root { get; init; }

    public JsonElement? Item { get; init; }

    public JsonElement? Parent { get; init; }

    public static DataScope FromRoot(JsonElement root) => new() { Root = root };

    public DataScope ForItem(JsonElement item) => new()
    {
        Root = Root,
        Item = item,
        Parent = Item
    };

    public bool TryGet(string path, out JsonElement value)
    {
        if (Item is not null && TryScopePrefix(path, out var head, out var rest))
        {
            JsonElement? source = head switch
            {
                "root" => Root,
                "item" => Item,
                "parent" => Parent,
                _ => null
            };
            if (source is null)
            {
                value = default;
                return false;
            }

            if (rest is null)
            {
                value = source.Value;
                return source.Value.ValueKind != JsonValueKind.Undefined;
            }

            return DataValues.TryResolve(source.Value, rest, out value);
        }

        return DataValues.TryResolve(Root, path, out value);
    }

    public static bool TryScopePrefix(string path, out string head, out string? rest)
    {
        var dot = path.IndexOf('.');
        head = dot < 0 ? path : path[..dot];
        if (head is not ("item" or "root" or "parent"))
        {
            rest = null;
            return false;
        }

        rest = dot < 0 ? null : path[(dot + 1)..];
        return true;
    }
}
