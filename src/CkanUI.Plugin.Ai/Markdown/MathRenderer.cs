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
/// LaTeX 数学公式渲染器：将数学公式转换为美观的 Avalonia 控件树（分数堆叠、上下标、根号、希腊字母与常用算子）。
/// </summary>
public static class MathRenderer
{
    public static readonly FontFamily MathFont = new("Cambria Math, Latin Modern Math, STIX Two Math, Times New Roman, DejaVu Serif, serif");

    private static readonly Dictionary<string, string> GreekAndSymbols = new(StringComparer.Ordinal)
    {
        ["\\alpha"] = "α", ["\\beta"] = "β", ["\\gamma"] = "γ", ["\\Gamma"] = "Γ",
        ["\\delta"] = "δ", ["\\Delta"] = "Δ", ["\\epsilon"] = "ε", ["\\zeta"] = "ζ",
        ["\\eta"] = "η", ["\\theta"] = "θ", ["\\Theta"] = "Θ", ["\\iota"] = "ι",
        ["\\kappa"] = "κ", ["\\lambda"] = "λ", ["\\Lambda"] = "Λ", ["\\mu"] = "μ",
        ["\\nu"] = "ν", ["\\xi"] = "ξ", ["\\pi"] = "π", ["\\Pi"] = "Π",
        ["\\rho"] = "ρ", ["\\sigma"] = "σ", ["\\Sigma"] = "Σ", ["\\tau"] = "τ",
        ["\\upsilon"] = "υ", ["\\phi"] = "φ", ["\\Phi"] = "Φ", ["\\chi"] = "χ",
        ["\\psi"] = "ψ", ["\\omega"] = "ω", ["\\Omega"] = "Ω",
        ["\\cdot"] = "·", ["\\times"] = "×", ["\\pm"] = "±", ["\\mp"] = "∓",
        ["\\div"] = "÷", ["\\le"] = "≤", ["\\leq"] = "≤", ["\\ge"] = "≥",
        ["\\geq"] = "≥", ["\\ne"] = "≠", ["\\neq"] = "≠", ["\\approx"] = "≈",
        ["\\equiv"] = "≡", ["\\infty"] = "∞", ["\\partial"] = "∂", ["\\nabla"] = "∇",
        ["\\sum"] = "∑", ["\\prod"] = "∏", ["\\int"] = "∫", ["\\oint"] = "∮",
        ["\\in"] = "∈", ["\\notin"] = "∉", ["\\subset"] = "⊂", ["\\cup"] = "∪",
        ["\\cap"] = "∩", ["\\to"] = "→", ["\\rightarrow"] = "→", ["\\leftarrow"] = "←",
        ["\\leftrightarrow"] = "↔", ["\\Rightarrow"] = "⇒", ["\\Leftarrow"] = "⇐",
        ["\\ln"] = "ln", ["\\log"] = "log", ["\\sin"] = "sin", ["\\cos"] = "cos",
        ["\\tan"] = "tan", ["\\exp"] = "exp", ["\\lim"] = "lim", ["\\min"] = "min",
        ["\\max"] = "max", ["\\quad"] = "  ", ["\\qquad"] = "    ", ["\\,"] = " ",
    };

    private static readonly Dictionary<char, char> SuperscriptMap = new()
    {
        ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴',
        ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹',
        ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽', [')'] = '⁾',
        ['n'] = 'ⁿ', ['i'] = 'ⁱ'
    };

    private static readonly Dictionary<char, char> SubscriptMap = new()
    {
        ['0'] = '₀', ['1'] = '₁', ['2'] = '₂', ['3'] = '₃', ['4'] = '₄',
        ['5'] = '₅', ['6'] = '₆', ['7'] = '₇', ['8'] = '₈', ['9'] = '₉',
        ['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍', [')'] = '₎',
        ['a'] = 'ₐ', ['e'] = 'ₑ', ['o'] = 'ₒ', ['x'] = 'ₓ', ['s'] = 'ₛ', ['p'] = 'ₚ', ['v'] = 'ᵥ'
    };

    /// <summary>
    /// 尝试将公式转换为直接排版的 Unicode 文本（当公式不包含上下分数堆叠或复杂开方时，优先以行内纯文字排版，彻底避免行高错位）。
    /// </summary>
    public static bool TryToUnicode(string formula, out string unicodeText)
    {
        unicodeText = string.Empty;
        if (string.IsNullOrWhiteSpace(formula)) return false;

        // 包含分式或开方时走控件排版
        if (formula.Contains("\\frac") || formula.Contains("\\sqrt"))
        {
            return false;
        }

        var text = PreprocessSymbols(formula);
        var sb = new System.Text.StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            if (text[i] == '^')
            {
                var exp = ExtractScript(text, i + 1, out var expLen);
                i += 1 + expLen;
                foreach (var ch in exp)
                {
                    if (SuperscriptMap.TryGetValue(ch, out var sup))
                    {
                        sb.Append(sup);
                    }
                    else
                    {
                        return false;
                    }
                }
                continue;
            }

            if (text[i] == '_')
            {
                var sub = ExtractScript(text, i + 1, out var subLen);
                i += 1 + subLen;
                foreach (var ch in sub)
                {
                    if (SubscriptMap.TryGetValue(ch, out var subCh))
                    {
                        sb.Append(subCh);
                    }
                    else
                    {
                        return false;
                    }
                }
                continue;
            }

            if (text[i] == '\\')
            {
                return false;
            }

            sb.Append(text[i]);
            i++;
        }

        unicodeText = sb.ToString().Trim();
        return !string.IsNullOrEmpty(unicodeText);
    }

    /// <summary>
    /// 渲染行内公式控件（紧凑排版）。
    /// </summary>
    public static Control RenderInline(string formula, double baseFontSize = 13.5)
    {
        var container = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1,
        };
        RenderFormulaInto(formula, container, baseFontSize);
        return container;
    }

    /// <summary>
    /// 渲染独立块级公式（居中、带底色卡片与复制公式按钮）。
    /// </summary>
    public static Control RenderBlock(string formula, double baseFontSize = 15.0)
    {
        var outerBorder = new Border
        {
            Margin = new Thickness(0, 8, 0, 8),
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        outerBorder.Bind(Border.BackgroundProperty, outerBorder.GetResourceObservable("CkanLayerRecessedBrush"));
        outerBorder.Bind(Border.BorderBrushProperty, outerBorder.GetResourceObservable("CkanCardBorderBrush"));
        outerBorder.BorderThickness = new Thickness(1);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var mathPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 2,
        };
        RenderFormulaInto(formula, mathPanel, baseFontSize);
        scroll.Content = mathPanel;
        Grid.SetColumn(scroll, 0);
        grid.Children.Add(scroll);

        // 复制按钮
        var copyButton = new Button
        {
            Content = "复制",
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(6, 2),
            FontSize = 11,
        };
        copyButton.Classes.Add("CkanGhost");
        copyButton.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(copyButton);
            if (top?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(formula);
                copyButton.Content = "已复制";
            }
        };
        Grid.SetColumn(copyButton, 1);
        grid.Children.Add(copyButton);

        outerBorder.Child = grid;
        return outerBorder;
    }

    private static void RenderFormulaInto(string formula, StackPanel container, double fontSize)
    {
        var replaced = PreprocessSymbols(formula);
        var i = 0;

        while (i < replaced.Length)
        {
            // 1. 分数 \frac{num}{den}
            if (TryMatchCommand(replaced, i, "\\frac", out var num, out var den, out var fracLen))
            {
                container.Children.Add(CreateFractionControl(num, den, fontSize));
                i += fracLen;
                continue;
            }

            // 2. 开方 \sqrt{radicand} 或 \sqrt[n]{radicand}
            if (TryMatchSqrt(replaced, i, out var degree, out var radicand, out var sqrtLen))
            {
                container.Children.Add(CreateSqrtControl(degree, radicand, fontSize));
                i += sqrtLen;
                continue;
            }

            // 3. 上标 ^2 或 ^{exp}
            if (replaced[i] == '^')
            {
                var exp = ExtractScript(replaced, i + 1, out var expLen);
                container.Children.Add(CreateScriptControl(exp, fontSize * 0.75, isSuper: true));
                i += 1 + expLen;
                continue;
            }

            // 4. 下标 _0 或 _{sub}
            if (replaced[i] == '_')
            {
                var sub = ExtractScript(replaced, i + 1, out var subLen);
                container.Children.Add(CreateScriptControl(sub, fontSize * 0.75, isSuper: false));
                i += 1 + subLen;
                continue;
            }

            // 5. 普通字符累积
            var start = i;
            while (i < replaced.Length && replaced[i] != '^' && replaced[i] != '_' && !replaced.Substring(i).StartsWith("\\frac") && !replaced.Substring(i).StartsWith("\\sqrt"))
            {
                i++;
            }
            var chunk = replaced.Substring(start, i - start);
            if (!string.IsNullOrEmpty(chunk))
            {
                container.Children.Add(CreateMathText(chunk, fontSize));
            }
        }
    }

    private static string PreprocessSymbols(string text)
    {
        var result = text;
        foreach (var (cmd, symbol) in GreekAndSymbols)
        {
            result = result.Replace(cmd, symbol);
        }
        return result;
    }

    private static bool TryMatchCommand(string text, int index, string command, out string arg1, out string arg2, out int length)
    {
        arg1 = arg2 = string.Empty;
        length = 0;
        if (!text.Substring(index).StartsWith(command)) return false;

        var p = index + command.Length;
        while (p < text.Length && char.IsWhiteSpace(text[p])) p++;

        if (p >= text.Length || text[p] != '{') return false;
        var a1 = ExtractBraced(text, p, out var len1);
        p += len1;

        while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
        if (p >= text.Length || text[p] != '{') return false;
        var a2 = ExtractBraced(text, p, out var len2);
        p += len2;

        arg1 = a1;
        arg2 = a2;
        length = p - index;
        return true;
    }

    private static bool TryMatchSqrt(string text, int index, out string degree, out string radicand, out int length)
    {
        degree = radicand = string.Empty;
        length = 0;
        if (!text.Substring(index).StartsWith("\\sqrt")) return false;

        var p = index + 5; // length of \sqrt
        while (p < text.Length && char.IsWhiteSpace(text[p])) p++;

        if (p < text.Length && text[p] == '[')
        {
            var close = text.IndexOf(']', p + 1);
            if (close > p)
            {
                degree = text.Substring(p + 1, close - p - 1);
                p = close + 1;
            }
        }

        while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
        if (p < text.Length && text[p] == '{')
        {
            radicand = ExtractBraced(text, p, out var len);
            p += len;
        }
        else if (p < text.Length)
        {
            radicand = text[p].ToString();
            p++;
        }

        length = p - index;
        return true;
    }

    private static string ExtractBraced(string text, int openBraceIndex, out int totalLength)
    {
        var depth = 1;
        var i = openBraceIndex + 1;
        while (i < text.Length && depth > 0)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;
            i++;
        }
        totalLength = i - openBraceIndex;
        return text.Substring(openBraceIndex + 1, Math.Max(0, i - openBraceIndex - 2));
    }

    private static string ExtractScript(string text, int startIndex, out int consumedLength)
    {
        if (startIndex >= text.Length)
        {
            consumedLength = 0;
            return string.Empty;
        }
        if (text[startIndex] == '{')
        {
            var content = ExtractBraced(text, startIndex, out consumedLength);
            return content;
        }
        consumedLength = 1;
        return text[startIndex].ToString();
    }

    private static Control CreateFractionControl(string numerator, string denominator, double baseFontSize)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
        };

        var numPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        RenderFormulaInto(numerator, numPanel, baseFontSize * 0.85);
        panel.Children.Add(numPanel);

        var bar = new Border
        {
            Height = 1.2,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 1, 0, 1),
        };
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("CkanTextPrimaryBrush"));
        panel.Children.Add(bar);

        var denPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        RenderFormulaInto(denominator, denPanel, baseFontSize * 0.85);
        panel.Children.Add(denPanel);

        return panel;
    }

    private static Control CreateSqrtControl(string degree, string radicand, double baseFontSize)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
        };

        if (!string.IsNullOrEmpty(degree))
        {
            var degText = new TextBlock
            {
                Text = degree,
                FontSize = baseFontSize * 0.65,
                FontFamily = MathFont,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, -2, 0),
            };
            degText.Bind(TextBlock.ForegroundProperty, degText.GetResourceObservable("CkanTextPrimaryBrush"));
            panel.Children.Add(degText);
        }

        var symbol = new TextBlock
        {
            Text = "√",
            FontSize = baseFontSize * 1.1,
            FontFamily = MathFont,
            VerticalAlignment = VerticalAlignment.Center,
        };
        symbol.Bind(TextBlock.ForegroundProperty, symbol.GetResourceObservable("CkanTextPrimaryBrush"));
        panel.Children.Add(symbol);

        var topBar = new Border
        {
            BorderThickness = new Thickness(0, 1.2, 0, 0),
            Padding = new Thickness(2, 1, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        topBar.Bind(Border.BorderBrushProperty, topBar.GetResourceObservable("CkanTextPrimaryBrush"));

        var contentPanel = new StackPanel { Orientation = Orientation.Horizontal };
        RenderFormulaInto(radicand, contentPanel, baseFontSize);
        topBar.Child = contentPanel;
        panel.Children.Add(topBar);

        return panel;
    }

    private static Control CreateScriptControl(string scriptText, double scriptFontSize, bool isSuper)
    {
        var text = new TextBlock
        {
            Text = scriptText,
            FontSize = scriptFontSize,
            FontFamily = MathFont,
            FontStyle = FontStyle.Italic,
            VerticalAlignment = isSuper ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Margin = isSuper ? new Thickness(0.5, 0, 1, 4) : new Thickness(0.5, 4, 1, 0),
        };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("CkanTextPrimaryBrush"));
        return text;
    }

    private static Control CreateMathText(string text, double fontSize)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontFamily = MathFont,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tb.Bind(TextBlock.ForegroundProperty, tb.GetResourceObservable("CkanTextPrimaryBrush"));
        return tb;
    }
}