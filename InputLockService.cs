using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace FirawH4K;

// Temporary, user-triggered input shield. Ctrl+Alt+F12 is interpreted inside
// the hook. Windows secure-attention controls remain available.
internal sealed class InputLockService : IDisposable
{
    private const int KeyboardHook = 13;
    private const int MouseHook = 14;
    private const int KeyDown = 0x0100;
    private const int KeyUp = 0x0101;
    private const int SysKeyDown = 0x0104;
    private const int SysKeyUp = 0x0105;
    private readonly Dispatcher dispatcher;
    private readonly HookCallback keyboardCallback;
    private readonly HookCallback mouseCallback;
    private nint keyboardHandle;
    private nint mouseHandle;
    private bool controlDown;
    private bool altDown;
    private bool stopping;
    private int cursorHides;

    public event Action? EmergencyStop;
    public bool IsActive => keyboardHandle != 0 && mouseHandle != 0;

    public InputLockService(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        keyboardCallback = KeyboardProc;
        mouseCallback = MouseProc;
    }

    public bool Start()
    {
        Stop();
        var module = GetModuleHandle(null);
        keyboardHandle = SetWindowsHookEx(KeyboardHook, keyboardCallback, module, 0);
        mouseHandle = SetWindowsHookEx(MouseHook, mouseCallback, module, 0);
        if (!IsActive)
        {
            Stop();
            return false;
        }
        for (var i = 0; i < 20; i++)
        {
            cursorHides++;
            if (ShowCursor(false) < 0) break;
        }
        return true;
    }

    public void Stop()
    {
        if (keyboardHandle != 0) { UnhookWindowsHookEx(keyboardHandle); keyboardHandle = 0; }
        if (mouseHandle != 0) { UnhookWindowsHookEx(mouseHandle); mouseHandle = 0; }
        while (cursorHides-- > 0) ShowCursor(true);
        cursorHides = 0;
        controlDown = altDown = stopping = false;
    }

    public void Dispose() => Stop();

    private nint KeyboardProc(int code, nint message, nint data)
    {
        if (code < 0 || !IsActive)
            return CallNextHookEx(keyboardHandle, code, message, data);
        var vk = Marshal.ReadInt32(data);
        var down = message == KeyDown || message == SysKeyDown;
        var up = message == KeyUp || message == SysKeyUp;
        if ((vk is 0x11 or 0xA2 or 0xA3) && (down || up)) controlDown = down;
        if ((vk is 0x12 or 0xA4 or 0xA5) && (down || up)) altDown = down;
        if (down && vk == 0x7B && controlDown && altDown && !stopping)
        {
            stopping = true;
            dispatcher.BeginInvoke(() => EmergencyStop?.Invoke());
        }
        return 1;
    }

    private nint MouseProc(int code, nint message, nint data) =>
        code >= 0 && IsActive ? 1 : CallNextHookEx(mouseHandle, code, message, data);

    private delegate nint HookCallback(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookCallback callback, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")]
    private static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);
}
