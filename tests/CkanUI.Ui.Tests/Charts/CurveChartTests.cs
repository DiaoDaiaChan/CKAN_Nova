using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using CkanUI.Ui.Charts;
using NUnit.Framework;

namespace CkanUI.Ui.Tests.Charts;

[TestFixture]
public sealed class CurveChartTests
{
    [SetUp]
    public void SetUp()
    {
        HeadlessHost.EnsureInitialized();
    }

    [Test]
    public void Properties_DefaultValuesAreExpected()
    {
        var chart = new CurveChart();

        Assert.Multiple(() =>
        {
            Assert.That(chart.HighlightedIndex, Is.EqualTo(-1));
            Assert.That(chart.HighlightedLabel, Is.Null);
            Assert.That(chart.PointClickCommand, Is.Null);
            Assert.That(chart.HighlightedStroke, Is.Null);
            Assert.That(chart.Series, Is.Null);
        });
    }

    [Test]
    public void HighlightedProperties_CanBeSetAndRead()
    {
        var chart = new CurveChart();
        chart.HighlightedIndex = 3;
        chart.HighlightedLabel = "1951-01-01";

        Assert.Multiple(() =>
        {
            Assert.That(chart.HighlightedIndex, Is.EqualTo(3));
            Assert.That(chart.HighlightedLabel, Is.EqualTo("1951-01-01"));
        });

        chart.HighlightedIndex = -1;
        chart.HighlightedLabel = null;

        Assert.Multiple(() =>
        {
            Assert.That(chart.HighlightedIndex, Is.EqualTo(-1));
            Assert.That(chart.HighlightedLabel, Is.Null);
        });
    }

    [Test]
    public void PointClickCommand_CanBeExecutedWithIndex()
    {
        var chart = new CurveChart();
        int? clickedIndex = null;
        var command = new RelayTestCommand(param =>
        {
            if (param is int idx)
            {
                clickedIndex = idx;
            }
        });

        chart.PointClickCommand = command;
        Assert.That(chart.PointClickCommand, Is.SameAs(command));

        chart.PointClickCommand.Execute(5);
        Assert.That(clickedIndex, Is.EqualTo(5));
    }

    private sealed class RelayTestCommand : ICommand
    {
        private readonly Action<object?> _execute;

        public RelayTestCommand(Action<object?> execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute(parameter);
    }
}
