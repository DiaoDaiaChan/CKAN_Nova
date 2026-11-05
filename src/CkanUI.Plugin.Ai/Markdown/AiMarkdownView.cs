using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace CkanUI.Plugin.Ai.Markdown;

/// <summary>
/// AI Markdown 视图控件：渲染富文本、LaTeX 数学公式 ($ 与 $$)、思维导图/流程图、代码块、表格与引用。
/// </summary>
public class AiMarkdownView : UserControl
{
    public static readonly StyledProperty<string?> MarkdownTextProperty =
        AvaloniaProperty.Register<AiMarkdownView, string?>(nameof(MarkdownText));

    public string? MarkdownText
    {
        get => GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    private readonly StackPanel _rootPanel;

    static AiMarkdownView()
    {
        MarkdownTextProperty.Changed.AddClassHandler<AiMarkdownView>((view, _) => view.UpdateContent());
    }

    public AiMarkdownView()
    {
        _rootPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
        };
        Content = _rootPanel;
    }

    private void UpdateContent()
    {
        _rootPanel.Children.Clear();
        var text = MarkdownText;
        if (string.IsNullOrWhiteSpace(text)) return;

        var doc = MarkdownParser.Parse(text);
        foreach (var block in doc.Blocks)
        {
            var control = RenderBlock(block);
            if (control is not null)
            {
                _rootPanel.Children.Add(control);
            }
        }
    }

    private Control? RenderBlock(MarkdownBlock block)
    {
        return block switch
        {
            HeadingBlock heading => RenderHeading(heading),
            ParagraphBlock para => RenderParagraph(para),
            CodeBlock code => RenderCodeBlock(code),
            BlockquoteBlock quote => RenderBlockquote(quote),
            ListBlock list => RenderList(list),
            TableBlock table => RenderTable(table),
            MathBlock math => MathRenderer.RenderBlock(math.Formula),
            MindmapBlock mindmap => MindmapRenderer.Render(mindmap.RawContent, mindmap.SyntaxType),
            HorizontalRuleBlock => RenderThematicBreak(),
            _ => null,
        };
    }

    private Control RenderHeading(HeadingBlock heading)
    {
        var (fontSize, weight, margin, hasBorder) = heading.Level switch
        {
            1 => (18.0, FontWeight.Bold, new Thickness(0, 10, 0, 4), true),
            2 => (16.0, FontWeight.Bold, new Thickness(0, 8, 0, 4), true),
            3 => (14.5, FontWeight.SemiBold, new Thickness(0, 6, 0, 2), false),
            4 => (13.5, FontWeight.SemiBold, new Thickness(0, 4, 0, 2), false),
            _ => (12.5, FontWeight.Medium, new Thickness(0, 3, 0, 2), false),
        };

        var tb = CreateInlinesTextBlock(heading.Inlines, fontSize, weight);
        tb.Margin = margin;

        if (!hasBorder)
        {
            return tb;
        }

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(tb);

        var line = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 2, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("CkanCardBorderBrush"));
        panel.Children.Add(line);

        return panel;
    }

    private Control RenderParagraph(ParagraphBlock para)
    {
        var tb = CreateInlinesTextBlock(para.Inlines, 13.0, FontWeight.Normal);
        tb.Margin = new Thickness(0, 2, 0, 4);
        return tb;
    }

    private SelectableTextBlock CreateInlinesTextBlock(IReadOnlyList<MarkdownInline> inlines, double fontSize, FontWeight weight)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontWeight = weight,
            // 勿设固定 LineHeight，交由 Avalonia TextLayout 自动计算基线与行高，彻底避免内联元素压扁文字
        };
        tb.Bind(SelectableTextBlock.ForegroundProperty, tb.GetResourceObservable("CkanTextPrimaryBrush"));

        AppendInlines(tb, tb.Inlines!, inlines, fontSize);
        return tb;
    }

    private void AppendInlines(SelectableTextBlock parentTb, InlineCollection target, IReadOnlyList<MarkdownInline> inlines, double baseFontSize)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextInline text:
                    target.Add(new Run(text.Text));
                    break;

                case BoldInline bold:
                    var boldSpan = new Span { FontWeight = FontWeight.Bold };
                    AppendInlines(parentTb, boldSpan.Inlines, bold.Inlines, baseFontSize);
                    target.Add(boldSpan);
                    break;

                case ItalicInline italic:
                    var italicSpan = new Span { FontStyle = FontStyle.Italic };
                    AppendInlines(parentTb, italicSpan.Inlines, italic.Inlines, baseFontSize);
                    target.Add(italicSpan);
                    break;

                case StrikethroughInline strike:
                    var strikeSpan = new Span { TextDecorations = TextDecorations.Strikethrough };
                    AppendInlines(parentTb, strikeSpan.Inlines, strike.Inlines, baseFontSize);
                    target.Add(strikeSpan);
                    break;

                case CodeInline code:
                    var codeRun = new Run($" {code.Code} ")
                    {
                        FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                        FontSize = baseFontSize * 0.92,
                        FontWeight = FontWeight.Normal,
                    };
                    codeRun.Bind(Run.ForegroundProperty, parentTb.GetResourceObservable("CkanAccentBrush"));
                    codeRun.Bind(Run.BackgroundProperty, parentTb.GetResourceObservable("CkanLayerRecessedBrush"));
                    target.Add(codeRun);
                    break;

                case MathInline math:
                    if (MathRenderer.TryToUnicode(math.Formula, out var unicodeMath))
                    {
                        var mathRun = new Run(unicodeMath)
                        {
                            FontFamily = MathRenderer.MathFont,
                            FontStyle = FontStyle.Italic,
                            FontSize = baseFontSize,
                        };
                        mathRun.Bind(Run.ForegroundProperty, parentTb.GetResourceObservable("CkanAccentBrush"));
                        target.Add(mathRun);
                    }
                    else
                    {
                        var mathCtrl = MathRenderer.RenderInline(math.Formula, baseFontSize);
                        target.Add(new InlineUIContainer(mathCtrl));
                    }
                    break;

                case LinkInline link:
                    var linkBtn = new Button
                    {
                        Content = link.Text,
                        Padding = new Thickness(1, 0),
                        Margin = new Thickness(0),
                        MinHeight = 0,
                        MinWidth = 0,
                        BorderThickness = new Thickness(0),
                        Background = Brushes.Transparent,
                        Cursor = new Cursor(StandardCursorType.Hand),
                        FontSize = baseFontSize,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    linkBtn.Classes.Add("CkanGhost");
                    linkBtn.Bind(Button.ForegroundProperty, parentTb.GetResourceObservable("CkanPrimaryBrush"));
                    linkBtn.Click += (_, _) =>
                    {
                        if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                            }
                            catch { }
                        }
                    };
                    target.Add(new InlineUIContainer(linkBtn));
                    break;
            }
        }
    }

    private Control RenderCodeBlock(CodeBlock code)
    {
        var outer = new Border
        {
            Margin = new Thickness(0, 6, 0, 6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
        };
        outer.Bind(Border.BackgroundProperty, outer.GetResourceObservable("CkanLayerRecessedBrush"));
        outer.Bind(Border.BorderBrushProperty, outer.GetResourceObservable("CkanCardBorderBrush"));

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
        };

        // Header bar with language & copy button
        var header = new Border
        {
            Padding = new Thickness(10, 5),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        header.Bind(Border.BackgroundProperty, header.GetResourceObservable("CkanLayerFloatingBrush"));
        header.Bind(Border.BorderBrushProperty, header.GetResourceObservable("CkanCardBorderBrush"));

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        var langText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(code.Language) ? "text" : code.Language,
            FontSize = 10.5,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        langText.Bind(TextBlock.ForegroundProperty, langText.GetResourceObservable("CkanTextSecondaryBrush"));
        Grid.SetColumn(langText, 0);
        headerGrid.Children.Add(langText);

        var copyBtn = new Button
        {
            Content = "复制代码",
            Padding = new Thickness(6, 2),
            FontSize = 11,
        };
        copyBtn.Classes.Add("CkanGhost");
        copyBtn.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(copyBtn);
            if (top?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(code.Code);
                copyBtn.Content = "已复制";
            }
        };
        Grid.SetColumn(copyBtn, 1);
        headerGrid.Children.Add(copyBtn);

        header.Child = headerGrid;
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        // Code content
        var tb = new TextBox
        {
            Text = code.Code,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 11.5,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 8),
        };
        Grid.SetRow(tb, 1);
        layout.Children.Add(tb);

        outer.Child = layout;
        return outer;
    }

    private Control RenderBlockquote(BlockquoteBlock quote)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 4, 0, 4),
            Padding = new Thickness(12, 6),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 4, 4, 0),
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("CkanLayerRecessedBrush"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("CkanPrimaryBrush"));

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        foreach (var child in quote.Children)
        {
            var c = RenderBlock(child);
            if (c is not null) stack.Children.Add(c);
        }
        border.Child = stack;
        return border;
    }

    private Control RenderList(ListBlock list)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 3,
            Margin = new Thickness(4, 2, 0, 4),
        };

        for (var i = 0; i < list.Items.Count; i++)
        {
            var item = list.Items[i];
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Margin = new Thickness(0, 1, 0, 1),
            };

            var bulletText = list.IsOrdered ? $"{list.StartNumber + i}." : "•";
            var bullet = new TextBlock
            {
                Text = bulletText,
                FontWeight = FontWeight.Bold,
                FontSize = 12.5,
                Margin = new Thickness(0, 1, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            bullet.Bind(TextBlock.ForegroundProperty, bullet.GetResourceObservable("CkanPrimaryBrush"));
            Grid.SetColumn(bullet, 0);
            row.Children.Add(bullet);

            var itemContent = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            var inlinesTb = CreateInlinesTextBlock(item.Inlines, 13.0, FontWeight.Normal);
            itemContent.Children.Add(inlinesTb);

            if (item.SubBlocks is { Count: > 0 })
            {
                foreach (var sub in item.SubBlocks)
                {
                    var c = RenderBlock(sub);
                    if (c is not null) itemContent.Children.Add(c);
                }
            }

            Grid.SetColumn(itemContent, 1);
            row.Children.Add(itemContent);

            stack.Children.Add(row);
        }

        return stack;
    }

    private Control RenderTable(TableBlock table)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 6, 0, 6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("CkanLayerRecessedBrush"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("CkanCardBorderBrush"));

        var tableStack = new StackPanel { Orientation = Orientation.Vertical };

        // Header row
        if (table.Headers.Count > 0)
        {
            var headerRow = CreateTableRow(table.Headers, isHeader: true);
            tableStack.Children.Add(headerRow);

            var sep = new Border
            {
                Height = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            sep.Bind(Border.BackgroundProperty, sep.GetResourceObservable("CkanCardBorderBrush"));
            tableStack.Children.Add(sep);
        }

        // Data rows
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var rowControl = CreateTableRow(row, isHeader: false, isEven: r % 2 == 1);
            tableStack.Children.Add(rowControl);

            if (r < table.Rows.Count - 1)
            {
                var line = new Border { Height = 0.8, HorizontalAlignment = HorizontalAlignment.Stretch };
                line.Bind(Border.BackgroundProperty, line.GetResourceObservable("CkanCardBorderBrush"));
                tableStack.Children.Add(line);
            }
        }

        border.Child = tableStack;
        return border;
    }

    private Control CreateTableRow(IReadOnlyList<string> cells, bool isHeader, bool isEven = false)
    {
        var rowBorder = new Border
        {
            Padding = new Thickness(10, 6),
        };
        if (isHeader)
        {
            rowBorder.Bind(Border.BackgroundProperty, rowBorder.GetResourceObservable("CkanLayerFloatingBrush"));
        }
        else if (isEven)
        {
            rowBorder.Bind(Border.BackgroundProperty, rowBorder.GetResourceObservable("CkanLayerSurfaceBrush"));
        }

        var cols = new ColumnDefinitions();
        for (var i = 0; i < cells.Count; i++)
        {
            cols.Add(new ColumnDefinition(1, GridUnitType.Star));
        }

        var grid = new Grid { ColumnDefinitions = cols };
        for (var i = 0; i < cells.Count; i++)
        {
            var cellText = cells[i];
            var cellInlines = MarkdownParser.ParseInlines(cellText);
            var tb = CreateInlinesTextBlock(cellInlines, isHeader ? 12.5 : 12.0, isHeader ? FontWeight.Bold : FontWeight.Normal);
            tb.Margin = new Thickness(4, 0, 4, 0);
            Grid.SetColumn(tb, i);
            grid.Children.Add(tb);
        }

        rowBorder.Child = grid;
        return rowBorder;
    }

    private Control RenderThematicBreak()
    {
        var sep = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 8, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        sep.Bind(Border.BackgroundProperty, sep.GetResourceObservable("CkanCardBorderBrush"));
        return sep;
    }
}