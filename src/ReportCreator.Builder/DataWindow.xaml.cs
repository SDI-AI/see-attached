using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ReportCreator.Model;

namespace ReportCreator.Builder;

public partial class DataWindow : Window
{
    private readonly DataDocument _document;
    private readonly HashSet<string> _open = [];
    private readonly List<Slot> _slots = [];
    private DataTreeNode? _shown;
    private bool _built;
    private bool _suppress = true;

    public DataWindow(DataDocument document)
    {
        InitializeComponent();
        _document = DataJson.Clone(document);
        TypeBox.ItemsSource = new[] { "string", "number", "date", "bool", "object" };
        RelationBox.ItemsSource = new[] { "One", "1:N" };
        BoolBox.ItemsSource = new[] { "true", "false" };
        _suppress = false;
        Reload("");
    }

    public DataDocument Document => _document;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FlushShown();
            ErrorText.Text = "";
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void NodeTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppress)
            return;
        if (NodeTree.SelectedItem is not DataTreeNode node || node == _shown)
            return;
        try
        {
            FlushShown();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            return;
        }

        Show(node);
    }

    private void AddChild_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DataTreeNode node } || !node.CanAdd)
            return;
        e.Handled = true;
        try
        {
            FlushShown();
            var parent = node.IsRoot ? null : node.FieldPath;
            var name = NextName(parent);
            DataEdit.Add(_document, parent, name, DataFieldType.String);
            ErrorText.Text = "";
            Reload(parent is null ? name : parent + "." + name);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_shown?.FieldPath is null)
            return;
        if (MessageBox.Show(this, "Delete this field?", "Data", MessageBoxButton.YesNo, MessageBoxImage.None) != MessageBoxResult.Yes)
            return;
        try
        {
            var parent = ParentPath(_shown.FieldPath) ?? "";
            DataEdit.Remove(_document, _shown.FieldPath);
            ErrorText.Text = "";
            Reload(parent);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void RootNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppress || _shown is not { IsRoot: true })
            return;
        var name = RootNameBox.Text.Trim();
        if (name.Length == 0)
            name = "Data";
        if (name.Length > 120)
            name = name[..120];
        _document.Name = name;
        RootNameBox.Text = name;
        _shown.Title = name;
    }

    private void NameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppress || _shown?.FieldPath is null)
            return;
        try
        {
            var path = ApplyName(_shown);
            ErrorText.Text = "";
            if (path != _shown.FieldPath)
                Reload(path);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _shown?.FieldPath is null)
            return;
        ApplyShape();
    }

    private void RelationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _shown?.FieldPath is null)
            return;
        if (TypeBox.SelectedItem as string != "object")
            return;
        ApplyShape();
    }

    private void ValueBox_LostFocus(object sender, RoutedEventArgs e) => ApplyValue();

    private void BoolBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress)
            return;
        ApplyValue();
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (_shown?.FieldPath is null)
            return;
        try
        {
            FlushItems();
            DataEdit.AddRow(_document, _shown.FieldPath);
            ErrorText.Text = "";
            Reload(_shown.Key);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Reload(string selectKey)
    {
        if (_built && NodeTree.ItemsSource is IEnumerable<DataTreeNode> current)
            Capture(current);
        Remember(selectKey);
        var root = new DataTreeNode { Key = "", IsRoot = true, CanAdd = true, Title = _document.Name, IsExpanded = true };
        AddFields(_document.Fields, "", root.Children);
        Mark(root, selectKey);
        _suppress = true;
        NodeTree.ItemsSource = new[] { root };
        _suppress = false;
        _built = true;
        Show(Find(root, selectKey) ?? root);
    }

    private void Show(DataTreeNode node)
    {
        _suppress = true;
        _shown = node;
        ErrorText.Text = "";
        if (node.IsRoot)
        {
            RootPanel.Visibility = Visibility.Visible;
            FieldPanel.Visibility = Visibility.Collapsed;
            RootNameBox.Text = _document.Name;
            _suppress = false;
            return;
        }

        RootPanel.Visibility = Visibility.Collapsed;
        FieldPanel.Visibility = Visibility.Visible;
        var field = DataEdit.Find(_document, node.FieldPath ?? "") ?? throw new ArgumentException("That field was not found.");
        NameBox.Text = field.Name;
        var many = field.Type == DataFieldType.Array;
        TypeBox.SelectedItem = many ? "object" : DataValues.TypeLabel(field.Type);
        RelationBox.SelectedItem = many ? "1:N" : "One";
        var onRow = node.FieldPath is not null && DataValues.UnderArray(_document, node.FieldPath);
        var showValue = !onRow && field.Type is DataFieldType.String or DataFieldType.Number or DataFieldType.Date or DataFieldType.Bool;
        var objectLike = many || field.Type == DataFieldType.Object;
        RelationLabel.Visibility = objectLike ? Visibility.Visible : Visibility.Collapsed;
        RelationBox.Visibility = objectLike ? Visibility.Visible : Visibility.Collapsed;
        ValueLabel.Visibility = showValue ? Visibility.Visible : Visibility.Collapsed;
        ValueBox.Visibility = showValue && field.Type != DataFieldType.Bool ? Visibility.Visible : Visibility.Collapsed;
        BoolBox.Visibility = showValue && field.Type == DataFieldType.Bool ? Visibility.Visible : Visibility.Collapsed;
        ValueLabel.Text = field.Type == DataFieldType.Date ? "Value (yyyy-MM-dd)" : "Value";
        if (showValue)
        {
            var current = DataValues.Text(_document.Values, node.FieldPath ?? "");
            ValueBox.Text = current;
            BoolBox.SelectedItem = current == "true" ? "true" : "false";
        }

        ItemsLabel.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        ItemsHost.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        AddItemButton.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        if (many)
            BuildItems(node.FieldPath ?? "");
        else
        {
            ItemsHost.Children.Clear();
            _slots.Clear();
        }

        NoteText.Text = many
            ? "1:N is a collection. Use + on this node to add a field every item has, including a nested collection."
            : field.Type == DataFieldType.Object
                ? "Use + on this node to add a nested field. Set that field to 1:N to make a collection such as invoice.items."
                : onRow ? "This field is on each item. Set the values on the 1:N node." : "";
        _suppress = false;
    }

    private void ApplyShape()
    {
        if (_shown?.FieldPath is null)
            return;
        try
        {
            var type = ChosenType();
            var field = DataEdit.Find(_document, _shown.FieldPath);
            if (field is null || field.Type == type)
            {
                Show(_shown);
                return;
            }

            DataEdit.SetType(_document, _shown.FieldPath, type);
            ErrorText.Text = "";
            Reload(_shown.Key);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void ApplyValue()
    {
        if (_suppress || _shown?.FieldPath is null)
            return;
        var field = DataEdit.Find(_document, _shown.FieldPath);
        if (field is null || DataValues.UnderArray(_document, _shown.FieldPath))
            return;
        if (field.Type is not (DataFieldType.String or DataFieldType.Number or DataFieldType.Date or DataFieldType.Bool))
            return;
        try
        {
            var text = field.Type == DataFieldType.Bool ? BoolBox.SelectedItem as string ?? "false" : ValueBox.Text;
            DataEdit.SetValue(_document, _shown.FieldPath, DataValues.Canonical(text, field.Type));
            _shown.Detail = Detail(field, _shown.FieldPath);
            ErrorText.Text = "";
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private string ApplyName(DataTreeNode node)
    {
        var path = node.FieldPath ?? throw new ArgumentException("Select a field.");
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
            throw new ArgumentException("Enter a name.");
        var field = DataEdit.Find(_document, path) ?? throw new ArgumentException("That field was not found.");
        if (field.Name == name)
            return path;
        DataEdit.Rename(_document, path, name);
        var parts = path.Split('.');
        parts[^1] = name;
        return string.Join('.', parts);
    }

    private void FlushShown()
    {
        if (_shown is null)
            return;
        if (_shown.IsRoot)
        {
            RootNameBox_LostFocus(RootNameBox, new RoutedEventArgs());
            return;
        }

        if (_shown.FieldPath is null)
            return;
        var path = ApplyName(_shown);
        if (path != _shown.FieldPath)
        {
            _shown.FieldPath = path;
            _shown.Key = path;
            _shown.Title = NameBox.Text.Trim();
        }

        var field = DataEdit.Find(_document, path);
        if (field is { Type: DataFieldType.Array })
            FlushItems();
        else if (field is not null && !DataValues.UnderArray(_document, path)
            && field.Type is DataFieldType.String or DataFieldType.Number or DataFieldType.Date or DataFieldType.Bool)
            ApplyValue();
    }

    private void FlushItems()
    {
        if (_shown?.FieldPath is null)
            return;
        foreach (var slot in _slots)
        {
            var text = slot.Bools is not null ? slot.Bools.SelectedItem as string ?? "false" : slot.Box!.Text;
            DataEdit.SetCell(_document, _shown.FieldPath, slot.Index, slot.Path, DataValues.Canonical(text, slot.Type));
        }
    }

    private void BuildItems(string path)
    {
        ItemsHost.Children.Clear();
        _slots.Clear();
        var field = DataEdit.Find(_document, path);
        if (field is null)
            return;
        var leaves = DataValues.Leaves(field.Fields, "").Where(leaf => leaf.Field.Type != DataFieldType.Array).ToList();
        if (leaves.Count == 0)
        {
            ItemsHost.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Use + on this collection to add the fields each item has.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4),
                Foreground = (System.Windows.Media.Brush)FindResource("Muted")
            });
        }

        var count = DataValues.RowCount(_document.Values, path);
        for (var index = 0; index < count; index++)
        {
            if (!DataValues.TryRow(_document.Values, path, index, out var row))
                continue;
            var head = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var remove = new Button { Content = "Delete", Style = (Style)FindResource("QuietButton"), Tag = index };
            remove.Click += DeleteItem_Click;
            DockPanel.SetDock(remove, Dock.Right);
            head.Children.Add(remove);
            head.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Item " + (index + 1).ToString(CultureInfo.InvariantCulture),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            ItemsHost.Children.Add(head);
            foreach (var leaf in leaves)
            {
                var label = new System.Windows.Controls.TextBlock { Text = leaf.Path };
                label.Style = (Style)FindResource("FieldLabel");
                ItemsHost.Children.Add(label);
                var current = DataValues.Text(row, leaf.Path);
                if (leaf.Field.Type == DataFieldType.Bool)
                {
                    var box = new ComboBox { ItemsSource = new[] { "true", "false" }, SelectedItem = current == "true" ? "true" : "false" };
                    box.Style = (Style)FindResource("FieldCombo");
                    ItemsHost.Children.Add(box);
                    _slots.Add(new Slot(index, leaf.Path, leaf.Field.Type, null, box));
                }
                else
                {
                    var box = new TextBox { Text = current };
                    box.Style = (Style)FindResource("FieldBox");
                    ItemsHost.Children.Add(box);
                    _slots.Add(new Slot(index, leaf.Path, leaf.Field.Type, box, null));
                }
            }
        }
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index } || _shown?.FieldPath is null)
            return;
        try
        {
            FlushItems();
            DataEdit.RemoveRow(_document, _shown.FieldPath, index);
            ErrorText.Text = "";
            Reload(_shown.Key);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void AddFields(List<DataField>? fields, string prefix, List<DataTreeNode> into)
    {
        foreach (var field in fields ?? [])
        {
            var path = prefix.Length == 0 ? field.Name : prefix + "." + field.Name;
            var node = new DataTreeNode
            {
                Key = path,
                FieldPath = path,
                CanAdd = field.Type is DataFieldType.Object or DataFieldType.Array,
                Title = field.Name,
                Detail = Detail(field, path),
                IsExpanded = !_built || _open.Contains(path)
            };
            if (field.Type is DataFieldType.Object or DataFieldType.Array)
                AddFields(field.Fields, path, node.Children);
            into.Add(node);
        }
    }

    private string Detail(DataField field, string path)
    {
        if (field.Type == DataFieldType.Array)
        {
            var count = DataValues.RowCount(_document.Values, path);
            return "  /  1:N  ·  " + count.ToString(CultureInfo.InvariantCulture);
        }

        var detail = "  /  " + DataValues.TypeLabel(field.Type);
        if (field.Type == DataFieldType.Object || DataValues.UnderArray(_document, path))
            return detail;
        var value = DataValues.Short(DataValues.Text(_document.Values, path), 24);
        return value.Length == 0 ? detail : detail + "  ·  " + value;
    }

    private void Capture(IEnumerable<DataTreeNode> nodes)
    {
        _open.Clear();
        void Walk(IEnumerable<DataTreeNode> list)
        {
            foreach (var node in list)
            {
                if (node.IsExpanded && node.FieldPath is not null)
                    _open.Add(node.FieldPath);
                Walk(node.Children);
            }
        }

        Walk(nodes);
    }

    private void Remember(string selectKey)
    {
        var path = selectKey;
        while (!string.IsNullOrEmpty(path))
        {
            _open.Add(path);
            path = ParentPath(path) ?? "";
        }
    }

    private static void Mark(DataTreeNode node, string selectKey)
    {
        node.IsSelected = node.Key == selectKey;
        foreach (var child in node.Children)
            Mark(child, selectKey);
    }

    private static DataTreeNode? Find(DataTreeNode node, string key)
    {
        if (node.Key == key)
            return node;
        foreach (var child in node.Children)
        {
            var found = Find(child, key);
            if (found is not null)
                return found;
        }

        return null;
    }

    private DataFieldType ChosenType()
    {
        var type = DataValues.ParseType(TypeBox.SelectedItem as string);
        if (type == DataFieldType.Object && RelationBox.SelectedItem as string == "1:N")
            return DataFieldType.Array;
        return type;
    }

    private string NextName(string? parent)
    {
        var siblings = string.IsNullOrWhiteSpace(parent)
            ? _document.Fields
            : DataEdit.Find(_document, parent)?.Fields ?? [];
        var used = siblings.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        for (var index = 1; ; index++)
        {
            var name = index == 1 ? "field" : "field" + index.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(name))
                return name;
        }
    }

    private static string? ParentPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        var parts = path.Split('.');
        return parts.Length == 1 ? null : string.Join('.', parts[..^1]);
    }

    private sealed record Slot(int Index, string Path, DataFieldType Type, TextBox? Box, ComboBox? Bools);
}
