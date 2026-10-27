using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs.Mods;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// F-35 虚拟模组（provides）选择对话框：某个依赖有多个提供者时让用户选一个。
/// 单选互斥、默认选中第一项（对齐上游 ChooseProvidedMods）；未选任何提供者时主按钮不提交，取消返回 null。
/// </summary>
public sealed partial class VirtualModuleDialogViewModel : DialogViewModelBase
{
    /// <summary>用请求数据构造对话框。</summary>
    public VirtualModuleDialogViewModel(VirtualModuleRequest request)
        : base("选择依赖的提供者")
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Providers);

        Message = request.Message;
        Dependency = request.Dependency;
        PrimaryButtonText = "使用所选";
        SecondaryButtonText = "取消";

        foreach (var provider in request.Providers)
        {
            var item = new VirtualModuleCandidateViewModel(provider);
            item.PropertyChanged += OnProviderChanged;
            Providers.Add(item);
        }

        if (Providers.Count > 0)
        {
            Providers[0].IsSelected = true;
        }
    }

    /// <summary>对话框顶部说明（通常来自上游 TooManyProvides 提示）。</summary>
    public string Message { get; }

    /// <summary>被多个模组提供的虚拟模组标识（可为空）。</summary>
    public string? Dependency { get; }

    /// <summary>是否知道虚拟模组标识。</summary>
    public bool HasDependency => !string.IsNullOrWhiteSpace(Dependency);

    /// <summary>候选提供者。</summary>
    public ObservableCollection<VirtualModuleCandidateViewModel> Providers { get; } = [];

    /// <summary>是否有候选提供者。</summary>
    public bool HasProviders => Providers.Count > 0;

    /// <summary>当前选中的提供者；没有则为 null。</summary>
    public VirtualModuleCandidateViewModel? SelectedProvider =>
        Providers.FirstOrDefault(p => p.IsSelected);

    /// <summary>当前选中提供者的摘要文案。</summary>
    public string SelectedText
    {
        get
        {
            var selected = SelectedProvider;
            return selected is null ? "尚未选择提供者。" : "已选择：" + selected.DisplayName;
        }
    }

    /// <summary>摘要文案。</summary>
    public string SummaryText => HasProviders
        ? "共 " + Providers.Count + " 个模组提供"
            + (HasDependency ? "「" + Dependency + "」" : "该虚拟模组")
            + "，请选择一个。"
        : "没有可选的提供者。";

    /// <summary>确认结果；取消时为 null（调用方应放弃安装）。</summary>
    public VirtualModuleChoice? Result { get; private set; }

    /// <summary>主按钮可用性（无候选时禁用）。</summary>
    public bool CanConfirm => HasProviders;

    /// <summary>选中某一行（单选互斥）；双击时直接确认。</summary>
    [RelayCommand]
    private void Activate(VirtualModuleCandidateViewModel? provider)
    {
        if (provider is null)
        {
            return;
        }

        provider.IsSelected = true;
        PrimaryCommand.Execute(null);
    }

    /// <inheritdoc />
    protected override bool Validate()
    {
        if (!HasProviders)
        {
            ValidationError = "没有可选的提供者，无法继续。";
            return false;
        }

        if (SelectedProvider is null)
        {
            ValidationError = "请先选择一个提供者。";
            return false;
        }

        ValidationError = null;
        return true;
    }

    /// <inheritdoc />
    protected override void OnPrimary()
    {
        var selected = SelectedProvider;
        Result = selected is null ? null : new VirtualModuleChoice(selected.Identifier, selected.Name);
    }

    /// <inheritdoc />
    protected override void OnSecondary() => Result = null;

    private void OnProviderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VirtualModuleCandidateViewModel.IsSelected))
        {
            return;
        }

        if (sender is not VirtualModuleCandidateViewModel changed)
        {
            return;
        }

        if (changed.IsSelected)
        {
            // 单选互斥：其它行一律取消。
            foreach (var provider in Providers)
            {
                if (!ReferenceEquals(provider, changed))
                {
                    provider.IsSelected = false;
                }
            }

            ValidationError = null;
        }

        OnPropertyChanged(nameof(SelectedProvider));
        OnPropertyChanged(nameof(SelectedText));
    }
}
