namespace CkanUI.TestSupport;

/// <summary>静默 IUser（07 篇 TST-01）：不弹任何框，Headless 为 true。</summary>
public sealed class FakeUser : CKAN.IUser
{
    public bool Headless => true;

    public bool RaiseYesNoDialog(string question) => false;

    public int RaiseSelectionDialog(string message, params object[] args) => -1;

    public void RaiseError(string message, params object[] args) { }

    public void RaiseProgress(string message, int percent) { }

    public void RaiseProgress(CKAN.ByteRateCounter rateCounter) { }

    public void RaiseMessage(string message, params object[] args) { }
}
