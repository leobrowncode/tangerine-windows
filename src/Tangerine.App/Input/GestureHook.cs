using System.Runtime.InteropServices;
using System.Windows.Threading;
using Tangerine.App.Native;
using Tangerine.App.Settings;
using static Tangerine.App.Native.NativeMethods;

namespace Tangerine.App.Input;

public enum WheelMode
{
    Convert,
    Actions,
}

/// <summary>
/// Watches global mouse and keyboard input for the Tangerine gesture: while a drag is in progress
/// (button held and moved past the system drag threshold), tapping a trigger key opens the wheel.
/// The hooks live on a dedicated thread so a busy UI thread can never stall system input.
/// </summary>
internal sealed class GestureHook : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly Thread _thread;
    private readonly HookProc _mouseProc;
    private readonly HookProc _keyboardProc;
    private readonly HashSet<uint> _swallowedKeys = [];
    private uint _threadId;
    private IntPtr _mouseHook, _keyboardHook;

    private bool _buttonDown;
    private bool _dragging;
    private POINT _downPoint;
    private POINT _lastPoint;
    private int _dragThresholdX = 4, _dragThresholdY = 4;

    private volatile TriggerKey _convertKey = TriggerKey.Shift;
    private volatile TriggerKey _actionsKey = TriggerKey.Control;

    public GestureHook(Dispatcher ui)
    {
        _ui = ui;
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "Tangerine input hooks" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>Raised on the UI thread with the cursor position in physical pixels.</summary>
    public event Action<WheelMode, POINT>? Triggered;

    /// <summary>Raised on the UI thread when the mouse button that started a drag is released.</summary>
    public event Action? DragEnded;

    public event Action? EscapePressed;

    public volatile bool Enabled = true;
    public volatile bool WheelVisible;

    public void Configure(TriggerKey convertKey, TriggerKey actionsKey)
    {
        _convertKey = convertKey;
        _actionsKey = actionsKey;
    }

    public void Start()
    {
        _thread.Start();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _dragThresholdX = Math.Max(2, GetSystemMetrics(SM_CXDRAG));
        _dragThresholdY = Math.Max(2, GetSystemMetrics(SM_CYDRAG));
        var module = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            // Low-level hooks only need the thread to pump; no windows live here.
        }

        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case WM_LBUTTONDOWN or WM_RBUTTONDOWN:
                    _buttonDown = true;
                    _dragging = false;
                    _downPoint = data.pt;
                    break;
                case WM_MOUSEMOVE:
                    _lastPoint = data.pt;
                    if (_buttonDown && !_dragging &&
                        (Math.Abs(data.pt.X - _downPoint.X) > _dragThresholdX || Math.Abs(data.pt.Y - _downPoint.Y) > _dragThresholdY))
                        _dragging = true;
                    break;
                case WM_LBUTTONUP or WM_RBUTTONUP:
                    var wasDragging = _dragging;
                    _buttonDown = false;
                    _dragging = false;
                    if (wasDragging) _ui.BeginInvoke(() => DragEnded?.Invoke());
                    break;
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        var message = (int)wParam;
        var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        var isUp = message is WM_KEYUP or WM_SYSKEYUP;
        var injected = (data.flags & LLKHF_INJECTED) != 0;

        if (isUp && _swallowedKeys.Remove(data.vkCode))
        {
            // The key-down was eaten so the drag stays a copy. The key-up still has to reach
            // Windows: eating it too leaves Shift latched in Explorer after the conversion,
            // and a click no longer selects a file until Esc and Shift.
            // SendInput from inside this hook deadlocks, so the extra key-up is posted to the UI thread.
            _ui.BeginInvoke(NativeMethods.ReleaseModifiersIfPhysicallyUp);
        }

        if (isDown && !injected)
        {
            if (data.vkCode == VK_ESCAPE && WheelVisible)
            {
                _ui.BeginInvoke(() => EscapePressed?.Invoke());
            }
            else if (Enabled && _dragging && ModeFor(data.vkCode) is { } mode)
            {
                // Swallow the key so the drag source does not switch to move/copy semantics.
                var isRepeat = !_swallowedKeys.Add(data.vkCode);
                if (!isRepeat)
                {
                    var point = _lastPoint;
                    _ui.BeginInvoke(() => Triggered?.Invoke(mode, point));
                }
                return 1;
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private WheelMode? ModeFor(uint vk)
    {
        if (Matches(_convertKey, vk)) return WheelMode.Convert;
        if (Matches(_actionsKey, vk)) return WheelMode.Actions;
        return null;
    }

    private static bool Matches(TriggerKey key, uint vk) => key switch
    {
        TriggerKey.Shift => vk is VK_SHIFT or VK_LSHIFT or VK_RSHIFT,
        TriggerKey.Control => vk is VK_CONTROL or VK_LCONTROL or VK_RCONTROL,
        TriggerKey.Alt => vk is VK_MENU or VK_LMENU or VK_RMENU,
        TriggerKey.CapsLock => vk == VK_CAPITAL,
        _ => false,
    };

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
