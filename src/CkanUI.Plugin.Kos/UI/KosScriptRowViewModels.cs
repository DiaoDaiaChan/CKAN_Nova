using System.Globalization;
using CkanUI.Plugin.Kos.Scripts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.Plugin.Kos.UI;

/// <summary>
/// 左栏脚本列表的一行。它只做 <see cref="KosScriptFile"/> 的展示投影：
/// 选中状态由页面维护，保存后由页面调用 <see cref="Update"/> 刷新大小/时间/行数。
/// </summary>
public sealed class KosScriptRowViewModel : ObservableObject
{
    private KosScriptFile _model;
    private bool _isSelected;

    public KosScriptRowViewModel(KosScriptFile model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public KosScriptFile Model => _model;

    public string Name => _model.Name;

    public string FullPath => _model.FullPath;

    public bool IsBoot => _model.IsBoot;

    public string SizeText => KosSizeText.Format(_model.SizeBytes);

    public string ModifiedText => _model.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public string LineCountText => _model.LineCount is { } lines ? $"{lines} 行" : "行数 —";

    public string DetailText => $"{SizeText} · {ModifiedText} · {LineCountText}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>保存/重新载入后刷新投影，避免列表还显示旧的大小和修改时间。</summary>
    public void Update(KosScriptFile model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Equals(_model, model))
        {
            return;
        }

        _model = model;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(IsBoot));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(ModifiedText));
        OnPropertyChanged(nameof(LineCountText));
        OnPropertyChanged(nameof(DetailText));
    }
}

/// <summary>体积文案只在列表行出现，格式集中一处，避免页面和行各算一套。</summary>
internal static class KosSizeText
{
    public static string Format(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return (bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        }

        if (bytes >= 1024L)
        {
            return (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        }

        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }
}
