using System.Runtime.InteropServices;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Input.GlobalHotkeys;

public sealed class KeyboardHookService : IDisposable
{
    private static readonly UIntPtr InjectionMarker = new(0x474E4F4D);

    private readonly Action<ShellAction> _dispatch;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private LowLevelKeyboardProc? _proc;

    private volatile Dictionary<(HotkeyModifiers, int), ShellAction> _bindings = new();
    private volatile bool _superAloneEnabled;
    private volatile bool _suspended;
    private volatile Action<Hotkey?>? _capture;

    private bool _lwinDown, _rwinDown, _winChord, _maskSent, _switcherActive;
    private readonly HashSet<int> _swallowedKeys = new();

    public KeyboardHookService(Action<ShellAction> dispatch) => _dispatch = dispatch;

    public bool IsRunning => _hook != IntPtr.Zero;

    public bool Suspended { get => _suspended; set => _suspended = value; }

    public void SetBindings(IEnumerable<HotkeyBinding> bindings)
    {
        var map = new Dictionary<(HotkeyModifiers, int), ShellAction>();
        bool superAlone = false;
        foreach (var b in bindings)
        {
            if (b.Hotkey.IsSuperAlone) { if (b.Action == ShellAction.ToggleOverview) superAlone = true; continue; }
            map[(b.Hotkey.Modifiers, b.Hotkey.Key)] = b.Action;
        }
        _bindings = map;
        _superAloneEnabled = superAlone;
    }

    public void BeginCapture(Action<Hotkey?> onCaptured) => _capture = onCaptured;
    public void CancelCapture() => _capture = null;

    public void Start()
    {
        if (_thread != null) return;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() => HookThread(ready)) { IsBackground = true, Name = "GnomeWin.KeyboardHook", Priority = ThreadPriority.Highest };
        _thread.Start();
        ready.Wait(3000);
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void Stop()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(2000);
        _thread = null;
    }

    private void HookThread(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _proc = HookProc;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Log.Error($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        else Log.Info("Keyboard hook installed.");
        ready.Set();

        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _lwinDown = _rwinDown = _switcherActive = false;
        _swallowedKeys.Clear();
        Log.Info("Keyboard hook removed.");
    }

    private unsafe IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);
        try
        {
            if (Process(wParam.ToInt32(), (KBDLLHOOKSTRUCT*)lParam)) return new IntPtr(1);
        }
        catch (Exception ex)
        {
            Log.Error("Keyboard hook error", ex);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private unsafe bool Process(int msg, KBDLLHOOKSTRUCT* kb)
    {
        if (kb->dwExtraInfo == InjectionMarker) return false;

        bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
        int vk = (int)kb->vkCode;
        bool isWin = vk == VK_LWIN || vk == VK_RWIN;

        var capture = _capture;
        if (capture != null && down && !IsModifier(vk))
        {
            _capture = null;
            _swallowedKeys.Add(vk);
            if (vk == VK_ESCAPE && CurrentModifiers() == HotkeyModifiers.None) capture(null);
            else
            {
                var mods = CurrentModifiers();
                if (mods.HasFlag(HotkeyModifiers.Super) || mods.HasFlag(HotkeyModifiers.Alt)) SendMask(ref _maskSent);
                capture(new Hotkey(mods, vk));
            }
            return true;
        }
        if (capture != null && up && isWin && !_winChord)
        {
            _capture = null;
            ReleaseWin(vk);
            capture(new Hotkey(HotkeyModifiers.Super, 0));
            return true;
        }

        if (isWin)
        {
            if (down)
            {
                if (!_lwinDown && !_rwinDown) { _winChord = false; _maskSent = false; }
                if (vk == VK_LWIN) _lwinDown = true; else _rwinDown = true;
                return false;
            }
            if (up)
            {
                if (vk == VK_LWIN) _lwinDown = false; else _rwinDown = false;
                if (_lwinDown || _rwinDown) return false;

                if (_switcherActive)
                {
                    _switcherActive = false;
                    _dispatch(ShellAction.SwitcherCommit);
                    return false;
                }
                if (!_winChord && _superAloneEnabled && !_suspended)
                {
                    ReleaseWin(vk);
                    _dispatch(ShellAction.ToggleOverview);
                    return true;
                }
            }
            return false;
        }

        bool winHeld = _lwinDown || _rwinDown;
        if (winHeld && down && !IsModifier(vk)) _winChord = true;

        if (up && _swallowedKeys.Remove(vk)) return true;
        if (!down || IsModifier(vk)) return false;

        if (_switcherActive && winHeld)
        {
            bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            ShellAction a = vk switch
            {
                VK_TAB => shift ? ShellAction.SwitcherPrevious : ShellAction.SwitcherNext,
                VK_RIGHT => ShellAction.SwitcherNext,
                VK_LEFT => ShellAction.SwitcherPrevious,
                VK_OEM_3 => ShellAction.SwitcherNextWindow,
                VK_ESCAPE => ShellAction.SwitcherCancel,
                _ => ShellAction.None,
            };
            if (a != ShellAction.None)
            {
                if (a == ShellAction.SwitcherCancel) _switcherActive = false;
                _swallowedKeys.Add(vk);
                _dispatch(a);
                return true;
            }
        }

        if (_suspended) return false;

        var modsNow = CurrentModifiers();
        if (!_bindings.TryGetValue((modsNow, vk), out ShellAction action)) return false;

        bool isRepeat = _swallowedKeys.Contains(vk);
        _swallowedKeys.Add(vk);
        if (modsNow.HasFlag(HotkeyModifiers.Super) || modsNow.HasFlag(HotkeyModifiers.Alt)) SendMask(ref _maskSent);

        if (action == ShellAction.AppSwitcher)
        {
            if (!winHeld)
            {
                _dispatch(ShellAction.AppSwitcher);
                _dispatch(ShellAction.SwitcherCommit);
                return true;
            }
            if (!isRepeat)
            {
                _switcherActive = true;
                _dispatch(ShellAction.AppSwitcher);
            }
            return true;
        }

        bool repeatable = action is ShellAction.WorkspaceNext or ShellAction.WorkspacePrevious;
        if (!isRepeat || repeatable) _dispatch(action);
        return true;
    }

    private HotkeyModifiers CurrentModifiers()
    {
        var m = HotkeyModifiers.None;
        if (_lwinDown || _rwinDown) m |= HotkeyModifiers.Super;
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) m |= HotkeyModifiers.Ctrl;
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0) m |= HotkeyModifiers.Alt;
        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) m |= HotkeyModifiers.Shift;
        return m;
    }

    private static bool IsModifier(int vk) =>
        vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LSHIFT or VK_RSHIFT or VK_LCONTROL or VK_RCONTROL
            or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN;

    private static void SendMask(ref bool alreadySent)
    {
        if (alreadySent) return;
        alreadySent = true;
        var inputs = new[] { Key(VK_MASK, false), Key(VK_MASK, true) };
        SendInput((uint)inputs.Length, inputs, INPUT.Size);
    }

    private void ReleaseWin(int winVk)
    {
        var inputs = new[] { Key(VK_MASK, false), Key(VK_MASK, true), Key(winVk, true, extended: true) };
        SendInput((uint)inputs.Length, inputs, INPUT.Size);
        _maskSent = true;
    }

    private static INPUT Key(int vk, bool keyUp, bool extended = false)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    dwFlags = (keyUp ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0),
                    dwExtraInfo = InjectionMarker,
                },
            },
        };
    }

    public static void SendChord(params int[] vks)
    {
        var list = new List<INPUT>();
        foreach (int vk in vks) list.Add(Key(vk, false, vk is VK_LWIN or VK_RWIN or VK_LEFT or VK_RIGHT or VK_UP or VK_DOWN));
        for (int i = vks.Length - 1; i >= 0; i--) list.Add(Key(vks[i], true, vks[i] is VK_LWIN or VK_RWIN or VK_LEFT or VK_RIGHT or VK_UP or VK_DOWN));
        SendInput((uint)list.Count, list.ToArray(), INPUT.Size);
    }

    public void Dispose() => Stop();
}
