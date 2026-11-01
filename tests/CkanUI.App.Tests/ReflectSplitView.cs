using System;
using System.Reflection;
using Avalonia.Controls;

namespace CkanUI.App.Tests;

class ReflectSplitView
{
    public static void Inspect()
    {
        var svType = typeof(SplitView);
        foreach (var m in svType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (m.Name.Contains("Pane") || m.Name.Contains("Close") || m.Name.Contains("Open"))
            {
                Console.WriteLine($"Method: {m.Name}");
            }
        }
        foreach (var f in svType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            Console.WriteLine($"Field: {f.Name} ({f.FieldType.Name})");
        }
    }
}
