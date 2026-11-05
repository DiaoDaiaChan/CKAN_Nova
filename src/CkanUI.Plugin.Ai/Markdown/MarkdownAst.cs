using System.Collections.Generic;

namespace CkanUI.Plugin.Ai.Markdown;

/// <summary>
/// Markdown 抽象语法树基类。
/// </summary>
public abstract record MarkdownNode;

/// <summary>块级元素基类。</summary>
public abstract record MarkdownBlock : MarkdownNode;

/// <summary>行内元素基类。</summary>
public abstract record MarkdownInline : MarkdownNode;

/// <summary>根文档。</summary>
public sealed record MarkdownDocument(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;

/// <summary>标题（1-6级）。</summary>
public sealed record HeadingBlock(int Level, IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>段落。</summary>
public sealed record ParagraphBlock(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>引用块。</summary>
public sealed record BlockquoteBlock(IReadOnlyList<MarkdownBlock> Children) : MarkdownBlock;

/// <summary>围栏代码块。</summary>
public sealed record CodeBlock(string Language, string Code) : MarkdownBlock;

/// <summary>独立块级数学公式（$$ ... $$）。</summary>
public sealed record MathBlock(string Formula) : MarkdownBlock;

/// <summary>思维导图或 Mermaid 图表块（mermaid, mindmap, diagram）。</summary>
public sealed record MindmapBlock(string SyntaxType, string RawContent) : MarkdownBlock;

/// <summary>列表块（有序/无序）。</summary>
public sealed record ListBlock(bool IsOrdered, int StartNumber, IReadOnlyList<ListItemBlock> Items) : MarkdownBlock;

/// <summary>列表项。</summary>
public sealed record ListItemBlock(IReadOnlyList<MarkdownInline> Inlines, IReadOnlyList<MarkdownBlock>? SubBlocks = null) : MarkdownBlock;

/// <summary>表格块（GFM 格式）。</summary>
public sealed record TableBlock(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) : MarkdownBlock;

/// <summary>水平分割线（--- / ***）。</summary>
public sealed record HorizontalRuleBlock : MarkdownBlock;

// ── 行内元素 ──────────────────────────────────────────────────────────

/// <summary>纯文本。</summary>
public sealed record TextInline(string Text) : MarkdownInline;

/// <summary>加粗文本（**text**）。</summary>
public sealed record BoldInline(IReadOnlyList<MarkdownInline> Inlines) : MarkdownInline;

/// <summary>斜体文本（*text* 或 _text_）。</summary>
public sealed record ItalicInline(IReadOnlyList<MarkdownInline> Inlines) : MarkdownInline;

/// <summary>删除线（~~text~~）。</summary>
public sealed record StrikethroughInline(IReadOnlyList<MarkdownInline> Inlines) : MarkdownInline;

/// <summary>行内代码。</summary>
public sealed record CodeInline(string Code) : MarkdownInline;

/// <summary>超链接（[text](url)）。</summary>
public sealed record LinkInline(string Text, string Url) : MarkdownInline;

/// <summary>行内数学公式（$formula$）。</summary>
public sealed record MathInline(string Formula) : MarkdownInline;
