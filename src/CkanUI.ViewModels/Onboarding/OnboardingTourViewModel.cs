using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Onboarding;

/// <summary>
/// 高亮指引单步目标区域类型。
/// </summary>
public enum OnboardingTargetType
{
    /// <summary>居中展示（欢迎/结束），不高亮特定控件。</summary>
    Center,
    /// <summary>左侧主导航栏（NavView）。</summary>
    NavigationSidebar,
    /// <summary>顶部导航控制栏（后退/前进/折叠按钮）。</summary>
    TitleBarControls,
    /// <summary>顶部子视图平级页签（SectionTabs）。</summary>
    SectionTabs,
    /// <summary>底部状态栏与后台任务进度胶囊（StatusBarTaskPill）。</summary>
    StatusBar
}

/// <summary>
/// 高亮指引步骤模型。
/// </summary>
public sealed class OnboardingStep
{
    public OnboardingStep(
        int index,
        string titleKey,
        string descriptionKey,
        OnboardingTargetType targetType,
        string? targetElementName = null)
    {
        Index = index;
        TitleKey = titleKey;
        DescriptionKey = descriptionKey;
        TargetType = targetType;
        TargetElementName = targetElementName;
    }

    public int Index { get; }
    public string TitleKey { get; }
    public string DescriptionKey { get; }
    public OnboardingTargetType TargetType { get; }
    public string? TargetElementName { get; }
}

/// <summary>
/// 首次启动高亮指引 ViewModel。
/// 驱动高亮指引的步进、跳转、跳过与完成，并提供多语言文本映射。
/// </summary>
public sealed partial class OnboardingTourViewModel : ObservableObject
{
    private readonly Func<string, string> _translate;
    private readonly Action? _onCompleted;

    public IReadOnlyList<OnboardingStep> Steps { get; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private int _currentStepIndex;

    [ObservableProperty]
    private OnboardingStep? _currentStep;

    [ObservableProperty]
    private string _currentTitle = string.Empty;

    [ObservableProperty]
    private string _currentDescription = string.Empty;

    [ObservableProperty]
    private string _stepProgressText = string.Empty;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _isLastStep;

    [ObservableProperty]
    private string _nextButtonText = string.Empty;

    public OnboardingTourViewModel(Func<string, string>? translate = null, Action? onCompleted = null)
    {
        _translate = translate ?? (key => key);
        _onCompleted = onCompleted;

        Steps = new List<OnboardingStep>
        {
            new(1, "Ui_Onboarding_Step1_Title", "Ui_Onboarding_Step1_Desc", OnboardingTargetType.Center),
            new(2, "Ui_Onboarding_Step2_Title", "Ui_Onboarding_Step2_Desc", OnboardingTargetType.NavigationSidebar, "NavView"),
            new(3, "Ui_Onboarding_Step3_Title", "Ui_Onboarding_Step3_Desc", OnboardingTargetType.TitleBarControls, "TitleBarNavToggleButton"),
            new(4, "Ui_Onboarding_Step4_Title", "Ui_Onboarding_Step4_Desc", OnboardingTargetType.SectionTabs, "SectionTabsControl"),
            new(5, "Ui_Onboarding_Step5_Title", "Ui_Onboarding_Step5_Desc", OnboardingTargetType.StatusBar, "StatusBarTaskPillBorder"),
            new(6, "Ui_Onboarding_Step6_Title", "Ui_Onboarding_Step6_Desc", OnboardingTargetType.Center)
        };
    }

    public void StartTour()
    {
        CurrentStepIndex = 0;
        IsActive = true;
        UpdateCurrentStep();
    }

    [RelayCommand]
    public void Next()
    {
        if (CurrentStepIndex < Steps.Count - 1)
        {
            CurrentStepIndex++;
            UpdateCurrentStep();
        }
        else
        {
            CompleteTour();
        }
    }

    [RelayCommand]
    public void Previous()
    {
        if (CurrentStepIndex > 0)
        {
            CurrentStepIndex--;
            UpdateCurrentStep();
        }
    }

    [RelayCommand]
    public void Skip()
    {
        CompleteTour();
    }

    private void CompleteTour()
    {
        IsActive = false;
        _onCompleted?.Invoke();
    }

    private void UpdateCurrentStep()
    {
        if (CurrentStepIndex >= 0 && CurrentStepIndex < Steps.Count)
        {
            CurrentStep = Steps[CurrentStepIndex];
            CurrentTitle = _translate(CurrentStep.TitleKey);
            CurrentDescription = _translate(CurrentStep.DescriptionKey);
            StepProgressText = $"{CurrentStepIndex + 1} / {Steps.Count}";
            CanGoBack = CurrentStepIndex > 0;
            IsLastStep = CurrentStepIndex == Steps.Count - 1;

            if (CurrentStepIndex == 0)
            {
                NextButtonText = _translate("Ui_Onboarding_Start");
            }
            else if (IsLastStep)
            {
                NextButtonText = _translate("Ui_Onboarding_Finish");
            }
            else
            {
                NextButtonText = _translate("Ui_Onboarding_Next");
            }
        }
    }
}
