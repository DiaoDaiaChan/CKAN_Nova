using System;
using CkanUI.ViewModels.Onboarding;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.Onboarding;

[TestFixture]
public sealed class OnboardingTourViewModelTests
{
    [Test]
    public void StartTour_InitializesAtStepZeroAndIsActive()
    {
        var vm = new OnboardingTourViewModel(key => $"Localized_{key}");
        Assert.That(vm.IsActive, Is.False);

        vm.StartTour();

        Assert.That(vm.IsActive, Is.True);
        Assert.That(vm.CurrentStepIndex, Is.EqualTo(0));
        Assert.That(vm.CurrentStep, Is.Not.Null);
        Assert.That(vm.CurrentStep!.Index, Is.EqualTo(1));
        Assert.That(vm.CurrentStep.TargetType, Is.EqualTo(OnboardingTargetType.Center));
        Assert.That(vm.CanGoBack, Is.False);
        Assert.That(vm.IsLastStep, Is.False);
        Assert.That(vm.StepProgressText, Is.EqualTo("1 / 6"));
        Assert.That(vm.CurrentTitle, Is.EqualTo("Localized_Ui_Onboarding_Step1_Title"));
        Assert.That(vm.NextButtonText, Is.EqualTo("Localized_Ui_Onboarding_Start"));
    }

    [Test]
    public void NextAndPrevious_NavigatesThroughAllSteps()
    {
        var vm = new OnboardingTourViewModel(key => key);
        vm.StartTour();

        // Step 1 -> Step 2
        vm.NextCommand.Execute(null);
        Assert.That(vm.CurrentStepIndex, Is.EqualTo(1));
        Assert.That(vm.CurrentStep!.TargetType, Is.EqualTo(OnboardingTargetType.NavigationSidebar));
        Assert.That(vm.CanGoBack, Is.True);
        Assert.That(vm.IsLastStep, Is.False);
        Assert.That(vm.NextButtonText, Is.EqualTo("Ui_Onboarding_Next"));

        // Step 2 -> Step 3
        vm.NextCommand.Execute(null);
        Assert.That(vm.CurrentStepIndex, Is.EqualTo(2));
        Assert.That(vm.CurrentStep!.TargetType, Is.EqualTo(OnboardingTargetType.TitleBarControls));

        // Step 3 -> Step 4
        vm.NextCommand.Execute(null);
        Assert.That(vm.CurrentStepIndex, Is.EqualTo(3));
        Assert.That(vm.CurrentStep!.TargetType, Is.EqualTo(OnboardingTargetType.SectionTabs));

        // Step 4 -> Step 5
        vm.NextCommand.Execute(null);
        Assert.That(vm.CurrentStepIndex, Is.EqualTo(4));
        Assert.That(vm.CurrentStep!.TargetType, Is.EqualTo(OnboardingTargetType.StatusBar));

        // Step 5 -> Step 6 (Final)
        vm.NextCommand.Execute(null);
        Assert.That(vm.CurrentStepIndex, Is.EqualTo(5));
        Assert.That(vm.CurrentStep!.TargetType, Is.EqualTo(OnboardingTargetType.Center));
        Assert.That(vm.IsLastStep, Is.True);
        Assert.That(vm.NextButtonText, Is.EqualTo("Ui_Onboarding_Finish"));

        // Back: Step 6 -> Step 5
        vm.PreviousCommand.Execute(null);
        Assert.That(vm.CurrentStepIndex, Is.EqualTo(4));
        Assert.That(vm.IsLastStep, Is.False);
    }

    [Test]
    public void SkipAndFinish_CompletesTourAndCallsCallback()
    {
        bool completed = false;
        var vm = new OnboardingTourViewModel(key => key, () => completed = true);
        vm.StartTour();

        Assert.That(vm.IsActive, Is.True);
        vm.SkipCommand.Execute(null);

        Assert.That(vm.IsActive, Is.False);
        Assert.That(completed, Is.True);

        // Test finish on last step
        completed = false;
        vm.StartTour();
        for (int i = 0; i < 5; i++)
        {
            vm.NextCommand.Execute(null);
        }
        Assert.That(vm.IsLastStep, Is.True);
        Assert.That(completed, Is.False);

        vm.NextCommand.Execute(null);
        Assert.That(vm.IsActive, Is.False);
        Assert.That(completed, Is.True);
    }
}
