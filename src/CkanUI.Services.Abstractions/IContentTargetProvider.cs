namespace CkanUI.Services.Abstractions;

/// <summary>提供当前内容页选中的模组标识符。</summary>
public interface IContentTargetProvider
{
    Task<string?> GetSelectedIdentifierAsync();
}
