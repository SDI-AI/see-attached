using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReportCreator.Model;
using ModelText = ReportCreator.Model.TextBlock;

namespace ReportCreator.Builder;

internal static class PropertyEditor
{
    public static void Fill(StackPanel panel, Block block, Action preview, Action rebuild)
    {
        panel.Children.Clear();
        AddLabel(panel, Kind(block));
        AddReadOnly(panel, "Id", block.Id);
        AddCheck(panel, "Keep together", block.KeepTogether, value =>
        {
            block.KeepTogether = value;
            preview();
        });

        switch (block)
        {
            case ModelText text:
                AddText(panel, "Text", text.Text, value => text.Text = value, preview, multiline: true);
                AddStyle(panel, text.FontSizePt, text.Bold, text.Color, text.Align,
                    size => text.FontSizePt = size,
                    bold => text.Bold = bold,
                    color => text.Color = color,
                    align => text.Align = align,
                    preview);
                break;
            case FieldBlock field:
                AddText(panel, "Path", field.Path, value => field.Path = value, preview, multiline: false);
                AddText(panel, "Format", field.Format ?? "", value => field.Format = Blank(value), preview, multiline: false);
                AddStyle(panel, field.FontSizePt, field.Bold, field.Color, field.Align,
                    size => field.FontSizePt = size,
                    bold => field.Bold = bold,
                    color => field.Color = color,
                    align => field.Align = align,
                    preview);
                break;
            case ImageBlock image:
                AddText(panel, "Source", image.Source, value => image.Source = value, preview, multiline: true);
                AddText(panel, "Alt", image.Alt, value => image.Alt = value, preview, multiline: false);
                AddOptionalMm(panel, "Width mm", image.WidthMm, value => image.WidthMm = value, preview);
                AddOptionalMm(panel, "Height mm", image.HeightMm, value => image.HeightMm = value, preview);
                break;
            case SpacerBlock spacer:
                AddMm(panel, "Height mm", spacer.HeightMm, value => spacer.HeightMm = value, preview);
                break;
            case ColumnsBlock columns:
                AddColumnTools(panel, columns, preview, rebuild);
                break;
            case TableBlock table:
                AddText(panel, "Data path", table.DataPath, value => table.DataPath = value, preview, multiline: false);
                AddCheck(panel, "Show header", table.ShowHeader, value =>
                {
                    table.ShowHeader = value;
                    preview();
                });
                AddTableColumns(panel, table, preview, rebuild);
                AddAggregates(panel, table, preview, rebuild);
                break;
        }
    }

    public static string Kind(Block block) => block switch
    {
        ModelText => "Text",
        FieldBlock => "Field",
        ImageBlock => "Image",
        SpacerBlock => "Spacer",
        ColumnsBlock => "Columns",
        TableBlock => "Table",
        _ => "Block"
    };

    private static void AddColumnTools(StackPanel panel, ColumnsBlock columns, Action preview, Action rebuild)
    {
        AddLabel(panel, $"{columns.Columns.Count} columns");
        var kind = new ComboBox { ItemsSource = new[] { "Text", "Field", "Spacer" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(kind);
        for (var index = 0; index < columns.Columns.Count; index++)
        {
            var columnIndex = index;
            var button = new Button
            {
                Content = $"Add to column {index + 1}",
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 4, 8, 4)
            };
            button.Click += (_, _) =>
            {
                var child = NewBlock(kind.SelectedItem as string ?? "Text");
                columns.Columns[columnIndex].Blocks.Add(child);
                rebuild();
                preview();
            };
            panel.Children.Add(button);
        }
    }

    private static void AddTableColumns(StackPanel panel, TableBlock table, Action preview, Action rebuild)
    {
        AddLabel(panel, "Columns");
        foreach (var column in table.Columns.ToList())
        {
            var box = new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 8) };
            var stack = new StackPanel();
            AddText(stack, "Header", column.Header, value => column.Header = value, preview, multiline: false);
            AddText(stack, "Path", column.Path, value => column.Path = value, preview, multiline: false);
            AddText(stack, "Format", column.Format ?? "", value => column.Format = Blank(value), preview, multiline: false);
            AddAlign(stack, column.Align, value => column.Align = value, preview);
            var remove = new Button { Content = "Remove column", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4) };
            remove.Click += (_, _) =>
            {
                table.Columns.Remove(column);
                rebuild();
                preview();
            };
            stack.Children.Add(remove);
            box.Child = stack;
            panel.Children.Add(box);
        }

        var add = new Button { Content = "Add column", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 12) };
        add.Click += (_, _) =>
        {
            table.Columns.Add(new TableColumn { Header = "Column", Path = "value" });
            rebuild();
            preview();
        };
        panel.Children.Add(add);
    }

    private static void AddAggregates(StackPanel panel, TableBlock table, Action preview, Action rebuild)
    {
        AddLabel(panel, "Aggregates");
        foreach (var aggregate in table.Aggregates.ToList())
        {
            AddText(panel, "Label", aggregate.Label ?? "", value => aggregate.Label = Blank(value), preview, multiline: false);
            AddText(panel, "Column path", aggregate.ColumnPath, value => aggregate.ColumnPath = value, preview, multiline: false);
            AddText(panel, "Format", aggregate.Format ?? "", value => aggregate.Format = Blank(value), preview, multiline: false);
            var kind = new ComboBox { ItemsSource = Enum.GetValues<AggregateKind>(), SelectedItem = aggregate.Kind, Margin = new Thickness(0, 0, 0, 8) };
            kind.SelectionChanged += (_, _) =>
            {
                if (kind.SelectedItem is AggregateKind value)
                {
                    aggregate.Kind = value;
                    preview();
                }
            };
            panel.Children.Add(kind);
            var remove = new Button { Content = "Remove aggregate", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 8) };
            remove.Click += (_, _) =>
            {
                table.Aggregates.Remove(aggregate);
                rebuild();
                preview();
            };
            panel.Children.Add(remove);
        }

        var add = new Button { Content = "Add sum", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4) };
        add.Click += (_, _) =>
        {
            table.Aggregates.Add(new TableAggregate
            {
                Label = "Total",
                ColumnPath = table.Columns.FirstOrDefault()?.Path ?? "",
                Kind = AggregateKind.Sum,
                Format = "0.##"
            });
            rebuild();
            preview();
        };
        panel.Children.Add(add);
    }

    private static void AddStyle(
        StackPanel panel,
        decimal fontSize,
        bool bold,
        string? color,
        TextAlign align,
        Action<decimal> setSize,
        Action<bool> setBold,
        Action<string?> setColor,
        Action<TextAlign> setAlign,
        Action preview)
    {
        AddMm(panel, "Font size pt", fontSize, setSize, preview);
        AddCheck(panel, "Bold", bold, value =>
        {
            setBold(value);
            preview();
        });
        AddText(panel, "Color", color ?? "", value => setColor(Blank(value)), preview, multiline: false);
        AddAlign(panel, align, setAlign, preview);
    }

    private static void AddAlign(StackPanel panel, TextAlign align, Action<TextAlign> set, Action preview)
    {
        AddLabel(panel, "Align");
        var box = new ComboBox { ItemsSource = Enum.GetValues<TextAlign>(), SelectedItem = align, Margin = new Thickness(0, 0, 0, 8) };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is TextAlign value)
            {
                set(value);
                preview();
            }
        };
        panel.Children.Add(box);
    }

    private static void AddText(StackPanel panel, string label, string value, Action<string> set, Action preview, bool multiline)
    {
        AddLabel(panel, label);
        var box = new TextBox
        {
            Text = value,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 72 : 0,
            Margin = new Thickness(0, 0, 0, 8)
        };
        box.TextChanged += (_, _) =>
        {
            set(box.Text);
            preview();
        };
        panel.Children.Add(box);
    }

    private static void AddMm(StackPanel panel, string label, decimal value, Action<decimal> set, Action preview)
    {
        AddLabel(panel, label);
        var box = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture), Margin = new Thickness(0, 0, 0, 8) };
        box.LostFocus += (_, _) =>
        {
            if (TryMm(box.Text, out var parsed))
            {
                set(parsed);
                box.ClearValue(Control.BorderBrushProperty);
                preview();
            }
            else
            {
                box.BorderBrush = Brushes.Firebrick;
            }
        };
        panel.Children.Add(box);
    }

    private static void AddOptionalMm(StackPanel panel, string label, decimal? value, Action<decimal?> set, Action preview)
    {
        AddLabel(panel, label);
        var box = new TextBox { Text = value?.ToString(CultureInfo.CurrentCulture) ?? "", Margin = new Thickness(0, 0, 0, 8) };
        box.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                set(null);
                box.ClearValue(Control.BorderBrushProperty);
                preview();
                return;
            }

            if (TryMm(box.Text, out var parsed))
            {
                set(parsed);
                box.ClearValue(Control.BorderBrushProperty);
                preview();
            }
            else
            {
                box.BorderBrush = Brushes.Firebrick;
            }
        };
        panel.Children.Add(box);
    }

    private static void AddCheck(StackPanel panel, string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 0, 8) };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        panel.Children.Add(box);
    }

    private static void AddReadOnly(StackPanel panel, string label, string value)
    {
        AddLabel(panel, label);
        panel.Children.Add(new TextBox { Text = value, IsReadOnly = true, Margin = new Thickness(0, 0, 0, 8) });
    }

    private static void AddLabel(Panel panel, string text) =>
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 4) });

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool TryMm(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static Block NewBlock(string kind) => kind switch
    {
        "Field" => new FieldBlock { Id = NewId(), Path = "name" },
        "Spacer" => new SpacerBlock { Id = NewId(), HeightMm = 6 },
        _ => new ModelText { Id = NewId(), Text = "Text" }
    };

    private static string NewId() => "b" + Guid.NewGuid().ToString("N")[..8];
}
