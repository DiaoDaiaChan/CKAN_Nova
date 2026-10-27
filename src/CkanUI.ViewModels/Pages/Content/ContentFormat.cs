using System.Globalization;
namespace CkanUI.ViewModels.Pages.Content;
internal static class ContentFormat
{
    public static string FormatSize(long bytes) => bytes < 1024 ? bytes.ToString(CultureInfo.InvariantCulture) + " B" : string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", bytes / Math.Pow(1024, Math.Floor(Math.Log(bytes, 1024))), new[] { "B", "KiB", "MiB", "GiB", "TiB", "PiB" }[(int)Math.Min(5, Math.Floor(Math.Log(bytes, 1024)))]);
}
