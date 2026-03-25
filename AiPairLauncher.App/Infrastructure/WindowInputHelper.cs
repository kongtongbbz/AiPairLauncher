using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AiPairLauncher.App.Infrastructure;

internal static class WindowInputHelper
{
    private const int SwRestore = 9;
    private const ushort VkReturn = 0x0D;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    public static void SendEnterKeyToProcessWindow(int processId)
    {
        using var process = Process.GetProcessById(processId);
        process.Refresh();

        var handle = process.MainWindowHandle;
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"无法获取 WezTerm 窗口句柄，进程 ID: {processId}");
        }

        ShowWindow(handle, SwRestore);
        if (!SetForegroundWindow(handle))
        {
            throw new InvalidOperationException("无法将 WezTerm 窗口置前，无法发送回车。");
        }

        var inputs = new[]
        {
            BuildKeyboardInput(VkReturn, 0),
            BuildKeyboardInput(VkReturn, KeyEventKeyUp),
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException("发送 Enter 按键失败。");
        }
    }

    private static INPUT BuildKeyboardInput(ushort virtualKey, uint flags)
    {
        return new INPUT
        {
            type = InputKeyboard,
            Anonymous = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = flags,
                },
            },
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION Anonymous;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
