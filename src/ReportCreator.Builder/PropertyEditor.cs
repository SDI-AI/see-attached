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
        AddCheck(panel, "Page break before", block.PageBreakBefore, value =>
        {
            block.PageBreakBefore = value;
            preview();
        });
        AddCheck(panel, "Page break after", block.PageBreakAfter, value =>
        {
            block.PageBreakAfter = value;
            preview();
        });
        AddShowWhen(panel, block, preview);

        switch (block)
        {
            case ModelText text:
                AddText(panel, "Text", text.Text, value => text.Text = value, preview, multiline: true);
                AddStyle(panel, text, preview);
                break;
            case FieldBlock field:
                AddText(panel, "Path", field.Path, value => field.Path = value, preview, multiline: false);
                AddText(panel, "Format", field.Format ?? "", value => field.Format = Blank(value), preview, multiline: false);
                AddStyle(panel, field.FontSizePt, field.Bold, field.Italic, field.Underline, field.FontFamily, field.LineHeight, field.Color, field.Align,
                    size => field.FontSizePt = size,
                    bold => field.Bold = bold,
                    italic => field.Italic = italic,
                    underline => field.Underline = underline,
                    family => field.FontFamily = family,
                    leading => field.LineHeight = leading,
                    color => field.Color = color,
                    align => field.Align = align,
                    preview);
                break;
            case RuleBlock rule:
                AddMm(panel, "Thickness mm", rule.ThicknessMm, value => rule.ThicknessMm = value, preview);
                AddText(panel, "Color", rule.Color ?? "", value => rule.Color = Blank(value), preview, multiline: false);
                AddMm(panel, "Margin top mm", rule.MarginTopMm, value => rule.MarginTopMm = value, preview);
                AddMm(panel, "Margin bottom mm", rule.MarginBottomMm, value => rule.MarginBottomMm = value, preview);
                break;
            case PanelBlock container:
                AddPanelTools(panel, container, preview, rebuild);
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
                AddText(panel, "Group by", table.GroupBy ?? "", value => table.GroupBy = Blank(value), preview, multiline: false);
                AddCheck(panel, "Break before group", table.BreakBeforeGroup, value =>
                {
                    table.BreakBeforeGroup = value;
                    preview();
                });
                AddCheck(panel, "Keep group together", table.KeepGroupTogether, value =>
                {
                    table.KeepGroupTogether = value;
                    preview();
                });
                AddCheck(panel, "Show header", table.ShowHeader, value =>
                {
                    table.ShowHeader = value;
                    preview();
                });
                AddTableColumns(panel, table, preview, rebuild);
                table.GroupAggregates ??= [];
                table.Aggregates ??= [];
                AddAggregateList(panel, "Group totals", table.GroupAggregates, table, preview, rebuild);
                AddAggregateList(panel, "Report totals", table.Aggregates, table, preview, rebuild);
                AddTableStyle(panel, table, preview);
                break;
        }

        AddBoxStyle(panel, block, preview);
    }

    public static string Kind(Block block) => block switch
    {
        ModelText => "Text",
        FieldBlock => "Field",
        ImageBlock => "Image",
        SpacerBlock => "Spacer",
        ColumnsBlock => "Columns",
        TableBlock => "Table",
        RuleBlock => "Rule",
        PanelBlock => "Panel",
        _ => "Block"
    };

    private static void AddPanelTools(StackPanel panel, PanelBlock container, Action preview, Action rebuild)
    {
        var kind = new ComboBox { ItemsSource = new[] { "Text", "Field", "Image", "Spacer", "Rule", "Table" }, SelectedIndex = 0 };
        Use(kind, "FieldCombo");
        panel.Children.Add(kind);
        var button = Quiet("Add inside panel");
        button.Click += (_, _) =>
        {
            container.Blocks.Add(NewBlock(kind.SelectedItem as string ?? "Text"));
            rebuild();
            preview();
        };
        panel.Children.Add(button);
    }

    private static void AddColumnTools(StackPanel panel, ColumnsBlock columns, Action preview, Action rebuild)
    {
        AddLabel(panel, $"{columns.Columns.Count} columns");
        var kind = new ComboBox { ItemsSource = new[] { "Text", "Field", "Spacer" }, SelectedIndex = 0 };
        Use(kind, "FieldCombo");
        panel.Children.Add(kind);
        for (var index = 0; index < columns.Columns.Count; index++)
        {
            var columnIndex = index;
            var button = Quiet($"Add to column {index + 1}");
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
            var box = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xD6, 0xCE)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var stack = new StackPanel();
            AddText(stack, "Header", column.Header, value => column.Header = value, preview, multiline: false);
            AddText(stack, "Path", column.Path, value => column.Path = value, preview, multiline: false);
            AddText(stack, "Format", column.Format ?? "", value => column.Format = Blank(value), preview, multiline: false);
            AddAlign(stack, column.Align, value => column.Align = value, preview);
            AddCheck(stack, "Running total", column.RunningTotal, value =>
            {
                column.RunningTotal = value;
                preview();
            });
            var remove = Quiet("Remove column");
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

        var add = Quiet("Add column");
        add.Click += (_, _) =>
        {
            table.Columns.Add(new TableColumn { Header = "Column", Path = "value" });
            rebuild();
            preview();
        };
        panel.Children.Add(add);
    }

    private static void AddShowWhen(StackPanel panel, Block block, Action preview)
    {
        var path = new TextBox { Text = block.When?.Path ?? "" };
        var kind = new ComboBox
        {
            ItemsSource = Enum.GetValues<ShowWhenKind>(),
            SelectedItem = block.When?.Kind ?? ShowWhenKind.NotEmpty
        };
        var value = new TextBox { Text = block.When?.Value ?? "" };
        Use(path, "FieldBox");
        Use(kind, "FieldCombo");
        Use(value, "FieldBox");
        AddLabel(panel, "Show when path");
        panel.Children.Add(path);
        AddLabel(panel, "Show when");
        panel.Children.Add(kind);
        AddLabel(panel, "Compare to");
        panel.Children.Add(value);

        void Apply()
        {
            ShowWhen? next = null;
            if (!string.IsNullOrWhiteSpace(path.Text))
            {
                next = new ShowWhen
                {
                    Path = path.Text.Trim(),
                    Kind = kind.SelectedItem is ShowWhenKind selected ? selected : ShowWhenKind.NotEmpty,
                    Value = string.IsNullOrWhiteSpace(value.Text) ? null : value.Text
                };
            }

            if (block.When?.Path == next?.Path && block.When?.Kind == next?.Kind && block.When?.Value == next?.Value)
                return;

            block.When = next;
            preview();
        }

        path.TextChanged += (_, _) => Apply();
        kind.SelectionChanged += (_, _) => Apply();
        value.TextChanged += (_, _) => Apply();
    }

    private static void AddAggregateList(StackPanel panel, string title, List<TableAggregate> aggregates, TableBlock table, Action preview, Action rebuild)
    {
        AddLabel(panel, title);
        foreach (var aggregate in aggregates.ToList())
        {
            AddText(panel, "Label", aggregate.Label ?? "", value => aggregate.Label = Blank(value), preview, multiline: false);
            AddText(panel, "Column path", aggregate.ColumnPath, value => aggregate.ColumnPath = value, preview, multiline: false);
            AddText(panel, "Format", aggregate.Format ?? "", value => aggregate.Format = Blank(value), preview, multiline: false);
            var kind = new ComboBox { ItemsSource = Enum.GetValues<AggregateKind>(), SelectedItem = aggregate.Kind };
            Use(kind, "FieldCombo");
            kind.SelectionChanged += (_, _) =>
            {
                if (kind.SelectedItem is AggregateKind value)
                {
                    aggregate.Kind = value;
                    preview();
                }
            };
            panel.Children.Add(kind);
            var remove = Quiet("Remove");
            remove.Click += (_, _) =>
            {
                aggregates.Remove(aggregate);
                rebuild();
                preview();
            };
            panel.Children.Add(remove);
        }

        var add = Quiet("Add sum");
        add.Click += (_, _) =>
        {
            aggregates.Add(new TableAggregate
            {
                Label = title == "Group totals" ? "Subtotal" : "Total",
                ColumnPath = table.Columns.FirstOrDefault()?.Path ?? "",
                Kind = AggregateKind.Sum,
                Format = "0.##"
            });
            rebuild();
            preview();
        };
        panel.Children.Add(add);
    }

    private static void AddStyle(StackPanel panel, ModelText text, Action preview)
    {
        AddStyle(panel, text.FontSizePt, text.Bold, text.Italic, text.Underline, text.FontFamily, text.LineHeight, text.Color, text.Align,
            size => text.FontSizePt = size,
            bold => text.Bold = bold,
            italic => text.Italic = italic,
            underline => text.Underline = underline,
            family => text.FontFamily = family,
            leading => text.LineHeight = leading,
            color => text.Color = color,
            align => text.Align = align,
            preview);
    }

    private static void AddStyle(
        StackPanel panel,
        decimal fontSize,
        bool bold,
        bool italic,
        bool underline,
        string? fontFamily,
        decimal? lineHeight,
        string? color,
        TextAlign align,
        Action<decimal> setSize,
        Action<bool> setBold,
        Action<bool> setItalic,
        Action<bool> setUnderline,
        Action<string?> setFamily,
        Action<decimal?> setLeading,
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
        AddCheck(panel, "Italic", italic, value =>
        {
            setItalic(value);
            preview();
        });
        AddCheck(panel, "Underline", underline, value =>
        {
            setUnderline(value);
            preview();
        });
        AddFontFamily(panel, fontFamily, setFamily, preview);
        AddOptionalMm(panel, "Line height", lineHeight, setLeading, preview);
        AddText(panel, "Color", color ?? "", value => setColor(Blank(value)), preview, multiline: false);
        AddAlign(panel, align, setAlign, preview);
    }

    private static void AddFontFamily(StackPanel panel, string? family, Action<string?> set, Action preview)
    {
        AddLabel(panel, "Font");
        var box = new ComboBox
        {
            ItemsSource = new[] { "", "Source Sans 3", "Arial", "Segoe UI", "Times New Roman", "Georgia", "Courier New" },
            SelectedItem = family ?? ""
        };
        Use(box, "FieldCombo");
        box.SelectionChanged += (_, _) =>
        {
            set(Blank(box.SelectedItem as string ?? ""));
            preview();
        };
        panel.Children.Add(box);
    }

    private static void AddBoxStyle(StackPanel panel, Block block, Action preview)
    {
        AddLabel(panel, "Box");
        var style = block.Style;
        AddText(panel, "Background", style?.Background ?? "", value =>
        {
            style = EnsureStyle(block);
            style.Background = Blank(value);
            Tidy(block);
            preview();
        }, preview, multiline: false);
        AddOptionalMm(panel, "Border mm", style?.Border?.WidthMm, value =>
        {
            if (value is null or <= 0)
            {
                if (block.Style is not null)
                    block.Style.Border = null;
            }
            else
            {
                style = EnsureStyle(block);
                style.Border ??= new BorderLine();
                style.Border.WidthMm = value.Value;
            }

            Tidy(block);
            preview();
        }, preview);
        AddText(panel, "Border color", style?.Border?.Color ?? "", value =>
        {
            style = EnsureStyle(block);
            style.Border ??= new BorderLine { WidthMm = 0.3m };
            style.Border.Color = Blank(value);
            Tidy(block);
            preview();
        }, preview, multiline: false);
        AddSpacing(panel, "Margin", () => block.Style?.Margin, value =>
        {
            var box = EnsureStyle(block);
            box.Margin = value;
            Tidy(block);
            preview();
        }, preview);
        AddSpacing(panel, "Padding", () => block.Style?.Padding, value =>
        {
            var box = EnsureStyle(block);
            box.Padding = value;
            Tidy(block);
            preview();
        }, preview);
    }

    private static void AddTableStyle(StackPanel panel, TableBlock table, Action preview)
    {
        AddLabel(panel, "Table style");
        var style = table.TableStyle;
        AddFontFamily(panel, style?.FontFamily, value =>
        {
            style = table.TableStyle ??= new TableStyle();
            style.FontFamily = value;
            TidyTable(table);
            preview();
        }, preview);
        AddOptionalMm(panel, "Table font pt", style?.FontSizePt, value =>
        {
            style = table.TableStyle ??= new TableStyle();
            style.FontSizePt = value;
            TidyTable(table);
            preview();
        }, preview);
        AddCheck(panel, "Table italic", style?.Italic ?? false, value =>
        {
            style = table.TableStyle ??= new TableStyle();
            style.Italic = value;
            TidyTable(table);
            preview();
        });
        AddOptionalMm(panel, "Cell padding mm", style?.CellPaddingMm, value =>
        {
            style = table.TableStyle ??= new TableStyle();
            style.CellPaddingMm = value;
            TidyTable(table);
            preview();
        }, preview);
        AddOptionalMm(panel, "Cell border mm", style?.BorderWidthMm, value =>
        {
            style = table.TableStyle ??= new TableStyle();
            style.BorderWidthMm = value;
            TidyTable(table);
            preview();
        }, preview);
        AddText(panel, "Cell border color", style?.BorderColor ?? "", value =>
        {
            style = table.TableStyle ??= new TableStyle();
            style.BorderColor = Blank(value);
            TidyTable(table);
            preview();
        }, preview, multiline: false);
        AddText(panel, "Header background", style?.HeaderBackground ?? "", value =>
        {
            style = table.TableStyle ??= new TableStyle();
            style.HeaderBackground = Blank(value);
            TidyTable(table);
            preview();
        }, preview, multiline: false);
    }

    private static void AddSpacing(StackPanel panel, string name, Func<Spacing?> current, Action<Spacing?> set, Action preview)
    {
        AddMm(panel, name + " top mm", current()?.Top ?? 0, value => set(CopySpacing(current(), top: value)), preview);
        AddMm(panel, name + " right mm", current()?.Right ?? 0, value => set(CopySpacing(current(), right: value)), preview);
        AddMm(panel, name + " bottom mm", current()?.Bottom ?? 0, value => set(CopySpacing(current(), bottom: value)), preview);
        AddMm(panel, name + " left mm", current()?.Left ?? 0, value => set(CopySpacing(current(), left: value)), preview);
    }

    private static Spacing? CopySpacing(Spacing? spacing, decimal? top = null, decimal? right = null, decimal? bottom = null, decimal? left = null)
    {
        var next = new Spacing
        {
            Top = top ?? spacing?.Top ?? 0,
            Right = right ?? spacing?.Right ?? 0,
            Bottom = bottom ?? spacing?.Bottom ?? 0,
            Left = left ?? spacing?.Left ?? 0
        };
        return next.Top == 0 && next.Right == 0 && next.Bottom == 0 && next.Left == 0 ? null : next;
    }

    private static BoxStyle EnsureStyle(Block block) => block.Style ??= new BoxStyle();

    private static void Tidy(Block block)
    {
        var style = block.Style;
        if (style is null)
            return;
        if (style.Margin is { Top: 0, Right: 0, Bottom: 0, Left: 0 })
            style.Margin = null;
        if (style.Padding is { Top: 0, Right: 0, Bottom: 0, Left: 0 })
            style.Padding = null;
        if (style.Border is { WidthMm: <= 0 })
            style.Border = null;
        if (style.FontFamily is null && style.FontSizePt is null && style.Bold is null && !style.Italic && !style.Underline
            && style.Color is null && style.LineHeight is null && style.Margin is null && style.Padding is null
            && style.Border is null && style.Background is null)
            block.Style = null;
    }

    private static void TidyTable(TableBlock table)
    {
        var style = table.TableStyle;
        if (style is null)
            return;
        if (style.FontFamily is null && style.FontSizePt is null && !style.Italic && style.Color is null
            && style.CellPaddingMm is null && style.BorderWidthMm is null && style.BorderColor is null
            && style.HeaderBackground is null && style.Background is null)
            table.TableStyle = null;
    }

    private static void AddAlign(StackPanel panel, TextAlign align, Action<TextAlign> set, Action preview)
    {
        AddLabel(panel, "Align");
        var box = new ComboBox { ItemsSource = Enum.GetValues<TextAlign>(), SelectedItem = align };
        Use(box, "FieldCombo");
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
        };
        Use(box, "FieldBox");
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
        var box = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture) };
        Use(box, "FieldBox");
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
        var box = new TextBox { Text = value?.ToString(CultureInfo.CurrentCulture) ?? "" };
        Use(box, "FieldBox");
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
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 8), Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17)) };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        panel.Children.Add(box);
    }

    private static void AddReadOnly(StackPanel panel, string label, string value)
    {
        AddLabel(panel, label);
        var box = new TextBox { Text = value, IsReadOnly = true };
        Use(box, "FieldBox");
        panel.Children.Add(box);
    }

    private static void AddLabel(Panel panel, string text)
    {
        var label = new System.Windows.Controls.TextBlock { Text = text };
        Use(label, "FieldLabel");
        panel.Children.Add(label);
    }

    private static Button Quiet(string content)
    {
        var button = new Button { Content = content, Margin = new Thickness(0, 0, 0, 8) };
        Use(button, "QuietButton");
        return button;
    }

    private static void Use(FrameworkElement element, string key)
    {
        if (Application.Current?.TryFindResource(key) is Style style)
            element.Style = style;
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool TryMm(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static Block NewBlock(string kind) => kind switch
    {
        "Field" => new FieldBlock { Id = NewId(), Path = "name" },
        "Image" => new ImageBlock { Id = NewId(), Source = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", Alt = "" },
        "Rule" => new RuleBlock { Id = NewId(), ThicknessMm = 0.3m },
        "Table" => new TableBlock
        {
            Id = NewId(),
            DataPath = "lines",
            Columns = [new TableColumn { Header = "Description", Path = "description" }]
        },
        "Spacer" => new SpacerBlock { Id = NewId(), HeightMm = 6 },
        _ => new ModelText { Id = NewId(), Text = "Text" }
    };

    private static string NewId() => "b" + Guid.NewGuid().ToString("N")[..8];
}
