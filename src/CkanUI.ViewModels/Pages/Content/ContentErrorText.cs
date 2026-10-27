namespace CkanUI.ViewModels.Pages.Content;
internal static class ContentErrorText
{
    public static string Compose(string? message) => string.IsNullOrWhiteSpace(message) ? "读取模组内容失败。" : message;
}
