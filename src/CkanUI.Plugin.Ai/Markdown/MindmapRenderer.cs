using Avalonia.Input.Platform;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CkanUI.Plugin.Ai.Markdown;

/// <summary>
/// 思维导图与流程图可视化渲染器：解析 mermaid/mindmap 语法并渲染为层次分明、带分支色谱与源码切换的视觉卡片。
/// </summary>
public static class MindmapRenderer
{
    private static readonly Color[] BranchColors = new[]
    {
        Color.FromRgb(59, 130, 246),  // Blue
        Color.FromRgb(16, 185, 129),  // Emerald
        Color.FromRgb(245, 158, 11),  // Amber
        Color.FromRgb(139, 92, 246),  // Purple
        Color.FromRgb(236, 72, 153),  // Pink
        Color.FromRgb(14, 165, 233),  // Sky
        Color.FromRgb(20, 184, 166),  // Teal
    };

    public sealed class Node
    {
        public string Text { get; set; } = string.Empty;
        public List<Node> Children { get; } = new();
    }

    public static Control Render(string source, string type = "mindmap")
    {
        var rootNode = ParseToTree(source);

        var outer = new Border
        {
            Margin = new Thickness(0, 8, 0, 8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
        };
        outer.Bind(Border.BackgroundProperty, outer.GetResourceObservable("CkanLayerRecessedBrush"));
        outer.Bind(Border.BorderBrushProperty, outer.GetResourceObservable("CkanCardBorderBrush"));

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
        };

        // Header bar
        var header = new Border
        {
            Padding = new Thickness(12, 8),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        header.Bind(Border.BackgroundProperty, header.GetResourceObservable("CkanLayerFloatingBrush"));
        header.Bind(Border.BorderBrushProperty, header.GetResourceObservable("CkanCardBorderBrush"));

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        var titlePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var titleText = new TextBlock
        {
            Text = type == "mindmap" ? "🧠 思维导图" : "📊 图表 / 流程",
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
        };
        titleText.Bind(TextBlock.ForegroundProperty, titleText.GetResourceObservable("CkanTextPrimaryBrush"));
        titlePanel.Children.Add(titleText);

        var langBadge = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1),
        };
        langBadge.Bind(Border.BackgroundProperty, langBadge.GetResourceObservable("CkanLayerRecessedBrush"));
        var langText = new TextBlock
        {
            Text = type,
            FontSize = 10,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
        };
        langText.Bind(TextBlock.ForegroundProperty, langText.GetResourceObservable("CkanTextSecondaryBrush"));
        langBadge.Child = langText;
        titlePanel.Children.Add(langBadge);

        Grid.SetColumn(titlePanel, 0);
        headerGrid.Children.Add(titlePanel);

        // Action buttons (Toggle Source, Copy)
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };

        var toggleBtn = new Button
        {
            Content = "查看源码",
            Padding = new Thickness(8, 2),
            FontSize = 11,
        };
        toggleBtn.Classes.Add("CkanGhost");

        var copyBtn = new Button
        {
            Content = "复制",
            Padding = new Thickness(8, 2),
            FontSize = 11,
        };
        copyBtn.Classes.Add("CkanGhost");
        copyBtn.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(copyBtn);
            if (top?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(source);
                copyBtn.Content = "已复制";
            }
        };

        actionsPanel.Children.Add(toggleBtn);
        actionsPanel.Children.Add(copyBtn);
        Grid.SetColumn(actionsPanel, 1);
        headerGrid.Children.Add(actionsPanel);
        header.Child = headerGrid;

        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        // Body area: visual vs raw code
        var visualView = CreateVisualTreeControl(rootNode);
        var rawCodeView = CreateRawCodeControl(source);
        rawCodeView.IsVisible = false;

        toggleBtn.Click += (_, _) =>
        {
            var showingCode = rawCodeView.IsVisible;
            rawCodeView.IsVisible = !showingCode;
            visualView.IsVisible = showingCode;
            toggleBtn.Content = showingCode ? "查看源码" : "显示图表";
        };

        var contentHost = new Grid();
        contentHost.Children.Add(visualView);
        contentHost.Children.Add(rawCodeView);

        Grid.SetRow(contentHost, 1);
        layout.Children.Add(contentHost);

        outer.Child = layout;
        return outer;
    }

    private static Control CreateVisualTreeControl(Node root)
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = new Thickness(14, 12),
        };

        var rootPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
        };

        // Root node header
        var rootBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1.5),
        };
        rootBorder.Bind(Border.BackgroundProperty, rootBorder.GetResourceObservable("CkanPrimaryMutedBrush"));
        rootBorder.Bind(Border.BorderBrushProperty, rootBorder.GetResourceObservable("CkanPrimaryBrush"));

        var rootText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(root.Text) ? "导图中心" : root.Text,
            FontWeight = FontWeight.Bold,
            FontSize = 13.5,
        };
        rootText.Bind(TextBlock.ForegroundProperty, rootText.GetResourceObservable("CkanPrimaryBrush"));
        rootBorder.Child = rootText;
        rootPanel.Children.Add(rootBorder);

        // Branches
        if (root.Children.Count > 0)
        {
            var branchesPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                Margin = new Thickness(16, 0, 0, 0),
            };

            for (var i = 0; i < root.Children.Count; i++)
            {
                var branch = root.Children[i];
                var color = BranchColors[i % BranchColors.Length];
                branchesPanel.Children.Add(CreateBranchControl(branch, color));
            }

            rootPanel.Children.Add(branchesPanel);
        }

        scroll.Content = rootPanel;
        return scroll;
    }

    private static Control CreateBranchControl(Node node, Color color)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = new SolidColorBrush(color),
            Padding = new Thickness(10, 2, 0, 2),
            Margin = new Thickness(0, 2, 0, 2),
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
        };

        // Node Title badge
        var titleBadge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(32, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
        };

        var title = new TextBlock
        {
            Text = node.Text,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(color),
        };
        titleBadge.Child = title;
        stack.Children.Add(titleBadge);

        // Recursive children
        if (node.Children.Count > 0)
        {
            var subPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Margin = new Thickness(14, 2, 0, 0),
            };

            foreach (var child in node.Children)
            {
                subPanel.Children.Add(CreateSubNodeControl(child, color));
            }
            stack.Children.Add(subPanel);
        }

        border.Child = stack;
        return border;
    }

    private static Control CreateSubNodeControl(Node node, Color color)
    {
        var container = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var bullet = new TextBlock
        {
            Text = "•",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(bullet);

        var text = new TextBlock
        {
            Text = node.Text,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("CkanTextPrimaryBrush"));
        row.Children.Add(text);
        container.Children.Add(row);

        if (node.Children.Count > 0)
        {
            var subStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 3,
                Margin = new Thickness(14, 2, 0, 0),
            };
            foreach (var child in node.Children)
            {
                subStack.Children.Add(CreateSubNodeControl(child, color));
            }
            container.Children.Add(subStack);
        }

        return container;
    }

    private static Control CreateRawCodeControl(string source)
    {
        var tb = new TextBox
        {
            Text = source,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 11.5,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Margin = new Thickness(8),
        };
        return tb;
    }

    public static Node ParseToTree(string source)
    {
        var lines = source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var root = new Node { Text = "主题" };

        var stack = new List<(int indent, Node node)> { (-1, root) };
        // isMermaidMindmap

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("mindmap", StringComparison.OrdinalIgnoreCase))
            {
                //
                continue;
            }
            if (trimmed.StartsWith("graph ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("flowchart ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var indent = CountLeadingIndent(line);
            var cleanText = CleanNodeText(trimmed);
            if (string.IsNullOrWhiteSpace(cleanText)) continue;

            // 流程图箭头连接 A --> B 处理
            if (cleanText.Contains("-->"))
            {
                var parts = cleanText.Split(new[] { "-->" }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var fromText = CleanNodeText(parts[0].Trim());
                    var toText = CleanNodeText(parts[1].Trim());
                    var fromNode = FindOrCreateChild(root, fromText);
                    fromNode.Children.Add(new Node { Text = toText });
                    continue;
                }
            }

            // 缩进式层级匹配
            while (stack.Count > 1 && stack[^1].indent >= indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var newNode = new Node { Text = cleanText };
            stack[^1].node.Children.Add(newNode);
            stack.Add((indent, newNode));
        }

        // 如果 root 只有一个根子节点且其自身为空，提升该子节点为根
        if (root.Children.Count == 1 && root.Text == "主题")
        {
            var single = root.Children[0];
            root.Text = single.Text;
            root.Children.Clear();
            root.Children.AddRange(single.Children);
        }

        return root;
    }

    private static Node FindOrCreateChild(Node parent, string text)
    {
        foreach (var child in parent.Children)
        {
            if (string.Equals(child.Text, text, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }
        var node = new Node { Text = text };
        parent.Children.Add(node);
        return node;
    }

    private static int CountLeadingIndent(string line)
    {
        var count = 0;
        foreach (var ch in line)
        {
            if (ch == ' ') count += 1;
            else if (ch == '\t') count += 4;
            else break;
        }
        return count;
    }

    private static string CleanNodeText(string raw)
    {
        var text = raw.Trim();
        // 移除 mermaid 形状包装：((text)), [text], (text), {text}, {{text}}
        if (text.StartsWith("root(", StringComparison.Ordinal) && text.EndsWith(")", StringComparison.Ordinal))
        {
            text = text.Substring(4).Trim('(', ')');
        }
        else if (text.Contains("[(") && text.EndsWith(")]", StringComparison.Ordinal))
        {
            var match = Regex.Match(text, @"\[\((.+?)\)\]");
            if (match.Success) text = match.Groups[1].Value;
        }
        else if (text.Contains("[[") && text.EndsWith("]]", StringComparison.Ordinal))
        {
            var match = Regex.Match(text, @"\[\[(.+?)\]\]");
            if (match.Success) text = match.Groups[1].Value;
        }
        else if (text.Contains("(") && text.EndsWith(")", StringComparison.Ordinal))
        {
            var match = Regex.Match(text, @"\((.+?)\)");
            if (match.Success) text = match.Groups[1].Value;
        }
        else if (text.Contains("[") && text.EndsWith("]", StringComparison.Ordinal))
        {
            var match = Regex.Match(text, @"\[(.+?)\]");
            if (match.Success) text = match.Groups[1].Value;
        }
        else if (text.Contains("{") && text.EndsWith("}", StringComparison.Ordinal))
        {
            var match = Regex.Match(text, @"\{(.+?)\}");
            if (match.Success) text = match.Groups[1].Value;
        }
        return text.Trim();
    }
}