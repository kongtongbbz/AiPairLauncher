using System.Runtime.ExceptionServices;
using System.Windows;
using AiPairLauncher.App.Controls;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class SafeTextBoxTests
{
    [Fact(DisplayName = "test_safe_textbox_supports_text_input_on_sta_thread")]
    public void SafeTextBoxSupportsTextInputOnStaThread()
    {
        RunInSta(() =>
        {
            var textBox = new SafeTextBox
            {
                Text = "中文输入",
            };

            Assert.Equal("中文输入", textBox.Text);
            Assert.Null(textBox.LastInputError);
        });
    }

    [Fact(DisplayName = "test_safe_textbox_can_rebind_input_handlers_after_reload")]
    public void SafeTextBoxCanRebindInputHandlersAfterReload()
    {
        RunInSta(() =>
        {
            var textBox = new SafeTextBox();
            textBox.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, textBox));
            textBox.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, textBox));
            textBox.Text = "reload";

            Assert.Equal("reload", textBox.Text);
            Assert.Null(textBox.LastInputError);
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? workerException = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                workerException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("STA 测试线程执行超时。");
        }

        if (workerException is not null)
        {
            ExceptionDispatchInfo.Capture(workerException).Throw();
        }
    }
}
