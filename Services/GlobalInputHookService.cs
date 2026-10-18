using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LemonAID.Services;

public sealed class GlobalInputHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;

    private const int VK_ESCAPE = 0x1B;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Keep strong references to prevent garbage collection while hooks are active
    private readonly LowLevelProc _mouseProc;
    private readonly LowLevelProc _keyboardProc;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private IntPtr _keyboardHookId = IntPtr.Zero;

    private bool _isLeftButtonDown;
    private POINT _currentCursorPos;
    private IntPtr _dragSourceHwnd = IntPtr.Zero;
    private bool _shiftHandledWhileDown;
    private bool _altHandledWhileDown;
    private bool _ctrlHandledWhileDown;

    public bool IsEnabled { get; set; } = true;
    public bool IsOverlayVisible { get; set; }

    public event Action<int, int, WheelMode, IntPtr>? OverlaySummoned;
    public event Action<WheelMode>? ModeChangeRequested;
    public event Action<int, int>? CursorMovedWhileActive;
    public event Action<int, int>? LeftButtonReleasedWhileActive;
    public event Action? DismissRequested;

    public GlobalInputHookService()
    {
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;
    }

    public void Start()
    {
        if (_mouseHookId != IntPtr.Zero) return;

        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule? curModule = curProcess.MainModule;
        IntPtr hMod = GetModuleHandle(curModule?.ModuleName);

        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
    }

    public void Stop()
    {
        if (_mouseHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
        if (_keyboardHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsEnabled)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            _currentCursorPos = hookStruct.pt;

            int msg = (int)wParam;
            if (msg == WM_LBUTTONDOWN)
            {
                _isLeftButtonDown = true;
                _dragSourceHwnd = GetForegroundWindow();
            }
            else if (msg == WM_MOUSEMOVE)
            {
                if (IsOverlayVisible)
                {
                    CursorMovedWhileActive?.Invoke(_currentCursorPos.X, _currentCursorPos.Y);
                }
            }
            else if (msg == WM_LBUTTONUP)
            {
                _isLeftButtonDown = false;
                _shiftHandledWhileDown = false;
                _altHandledWhileDown = false;
                _ctrlHandledWhileDown = false;

                if (IsOverlayVisible)
                {
                    LeftButtonReleasedWhileActive?.Invoke(_currentCursorPos.X, _currentCursorPos.Y);
                }
            }
        }

        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsEnabled)
        {
            int msg = (int)wParam;
            int vkCode = Marshal.ReadInt32(lParam);

            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            bool isShift = vkCode is VK_SHIFT or VK_LSHIFT or VK_RSHIFT;
            bool isAlt = vkCode is VK_MENU or VK_LMENU or VK_RMENU;
            bool isCtrl = vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL;

            if (isKeyUp)
            {
                if (isShift) _shiftHandledWhileDown = false;
                if (isAlt) _altHandledWhileDown = false;
                if (isCtrl) _ctrlHandledWhileDown = false;
            }
            else if (isKeyDown)
            {
                if (vkCode == VK_ESCAPE && IsOverlayVisible)
                {
                    DismissRequested?.Invoke();
                }
                else if (_isLeftButtonDown || IsOverlayVisible)
                {
                    if (isShift && !_shiftHandledWhileDown)
                    {
                        _shiftHandledWhileDown = true;
                        TriggerOrSwitchMode(WheelMode.Converter);
                    }
                    else if (isAlt && !_altHandledWhileDown)
                    {
                        _altHandledWhileDown = true;
                        TriggerOrSwitchMode(WheelMode.Tools);
                    }
                    else if (isCtrl && !_ctrlHandledWhileDown)
                    {
                        _ctrlHandledWhileDown = true;
                        TriggerOrSwitchMode(WheelMode.Actions);
                    }
                }
            }
        }

        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private void TriggerOrSwitchMode(WheelMode mode)
    {
        if (!IsOverlayVisible)
        {
            GetCursorPos(out _currentCursorPos);
            OverlaySummoned?.Invoke(_currentCursorPos.X, _currentCursorPos.Y, mode, _dragSourceHwnd);
        }
        else
        {
            ModeChangeRequested?.Invoke(mode);
        }
    }

    /// <summary>
    /// Queries Windows Shell COM automation to retrieve the currently selected file(s) or folder(s)
    /// from the active File Explorer window when the user clicks and holds on an item.
    /// </summary>
    public static List<string> TryGetSelectedPathsFromExplorer(IntPtr targetHwnd)
    {
        var results = new List<string>();
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return results;

            object? shell = Activator.CreateInstance(shellType);
            if (shell == null) return results;

            object? windows = shellType.InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null);
            if (windows == null) return results;

            int count = Convert.ToInt32(windows.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, windows, null));
            for (int i = 0; i < count; i++)
            {
                object? item = windows.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, windows, [i]);
                if (item == null) continue;

                long hwndVal = Convert.ToInt64(item.GetType().InvokeMember("HWND", BindingFlags.GetProperty, null, item, null));
                if (targetHwnd != IntPtr.Zero && new IntPtr(hwndVal) != targetHwnd)
                {
                    continue;
                }

                object? doc = item.GetType().InvokeMember("Document", BindingFlags.GetProperty, null, item, null);
                if (doc == null) continue;

                object? selectedItems = doc.GetType().InvokeMember("SelectedItems", BindingFlags.InvokeMethod, null, doc, null);
                if (selectedItems == null) continue;

                int selCount = Convert.ToInt32(selectedItems.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, selectedItems, null));
                for (int j = 0; j < selCount; j++)
                {
                    object? folderItem = selectedItems.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, selectedItems, [j]);
                    if (folderItem == null) continue;

                    string? path = folderItem.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, folderItem, null) as string;
                    if (!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
                    {
                        results.Add(path);
                    }
                }

                if (results.Count > 0) break;
            }
        }
        catch
        {
            // Ignore COM automation errors if the click originated outside File Explorer
        }
        return results;
    }

    public void Dispose()
    {
        Stop();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
