using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class PlayTimePageViewModelTests
{
    private sealed class FakePlayTimeService : IPlayTimeService
    {
        public List<PlayTimeEntry> Items { get; } = new(); public int Saves { get; private set; }
        public IReadOnlyList<PlayTimeEntry> Load() => Items;
        public bool Save(string name, double hours) { Saves++; Items.RemoveAll(x => x.InstanceName == name); Items.Add(new(name, hours)); return true; }
    }
    [Test] public void LoadsEmptyAndAllowsEditCommitCancelAndInvalidInput()
    {
        var service = new FakePlayTimeService(); var vm = new PlayTimePageViewModel(service); vm.RefreshCommand.Execute(null); Assert.That(vm.ShowEmptyState, Is.True);
        service.Items.Add(new("KSP", 8)); vm.RefreshCommand.Execute(null); var row = vm.Entries.Single(); vm.BeginEditCommand.Execute(row); Assert.That(row.IsEditing, Is.True); row.EditText = "12.6"; vm.CommitEditCommand.Execute(row); Assert.That(row.Hours, Is.EqualTo(13)); Assert.That(service.Saves, Is.EqualTo(1));
        vm.BeginEditCommand.Execute(row); row.EditText = "-2"; vm.CommitEditCommand.Execute(row); Assert.That(row.IsEditing, Is.True); Assert.That(service.Saves, Is.EqualTo(1)); Assert.That(vm.ErrorText, Is.Not.Null); vm.CancelEditCommand.Execute(row); Assert.That(row.IsEditing, Is.False);
    }
}
