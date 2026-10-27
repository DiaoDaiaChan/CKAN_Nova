using System.Collections.Generic;

namespace CkanUI.Services.Models;

public sealed class SavedSearch
{
    public string Name { get; set; } = "";
    public List<string> Values { get; set; } = new();
}
