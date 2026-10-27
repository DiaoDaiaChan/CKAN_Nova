namespace CkanUI.ModText;

/// <summary>Mod 元数据译文查找（纯函数，无 IO；17 篇）。</summary>
public interface IModTextProvider
{
    ModText? Find(string identifier, string field, string sourceText);
}

/// <summary>译文 + 来源 + 是否过期。</summary>
public sealed record ModText(string Text, ModTextSource Source, bool Stale);

public enum ModTextSource
{
    None = 0,
    Machine = 1,
    Human = 2,
}
