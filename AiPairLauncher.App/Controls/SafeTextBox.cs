using System.Diagnostics;
using System.Windows.Input;

namespace AiPairLauncher.App.Controls;

public class SafeTextBox : System.Windows.Controls.TextBox
{
    public Exception? LastInputError { get; private set; }

    public SafeTextBox()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        TextCompositionManager.AddPreviewTextInputStartHandler(this, OnPreviewTextInputStartSafe);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(this, OnPreviewTextInputUpdateSafe);
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        TextCompositionManager.RemovePreviewTextInputStartHandler(this, OnPreviewTextInputStartSafe);
        TextCompositionManager.RemovePreviewTextInputUpdateHandler(this, OnPreviewTextInputUpdateSafe);
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (!TryRun(() => base.OnPreviewTextInput(e), "OnPreviewTextInput"))
        {
            e.Handled = true;
        }
    }

    protected override void OnTextChanged(System.Windows.Controls.TextChangedEventArgs e)
    {
        TryRun(() => base.OnTextChanged(e), "OnTextChanged");
    }

    private void OnPreviewTextInputStartSafe(object sender, TextCompositionEventArgs e)
    {
        if (!TryRun(() => { }, "OnPreviewTextInputStart"))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewTextInputUpdateSafe(object sender, TextCompositionEventArgs e)
    {
        if (!TryRun(() => { }, "OnPreviewTextInputUpdate"))
        {
            e.Handled = true;
        }
    }

    private bool TryRun(Action action, string stage)
    {
        try
        {
            action();
            LastInputError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastInputError = ex;
            Trace.WriteLine($"[SafeTextBox] 捕获输入异常 ({stage}): {ex}");
            return false;
        }
    }
}
