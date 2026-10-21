using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace Halora.App;

/// <summary>
/// Watches the mouse system-wide with a low-level hook and reports "the left button is held,
/// the mouse has moved, and Shift is down". Windows has no public event for "a file is being
/// dragged somewhere", so this is the signal Halora uses to show its drop catcher.
///
/// The hook runs on its own thread with its own message loop. Windows silently removes
/// low-level hooks that answer slowly, so the callback only records state and posts events
/// to the WPF dispatcher; it never touches UI.
/// </summary>
internal sealed class DragWatcher : IDisposable
{
    private const int StartDistance = 12; // pixels the mouse must travel before it counts as a drag
    private const uint WM_QUIT = 0x0012;

    private readonly Dispatcher _ui;
    private readonly Native.LowLevelMouseProc _proc; // kept in a field so the GC can't collect it
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;

    private bool _buttonDown;
    private bool _armed;
    private Native.POINT _downAt;
    private Native.POINT _latest;
    private int _movePending;

    public DragWatcher(Dispatcher ui)
    {
        _ui = ui;
        _proc = HookCallback;
    }

    /// <summary>Shift + drag began at this screen point (physical pixels).</summary>
    public event Action<Native.POINT>? ShiftDragStarted;

    /// <summary>The cursor moved while armed.</summary>
    public event Action<Native.POINT>? ShiftDragMoved;

    /// <summary>Shift was released before the mouse button.</summary>
    public event Action? ShiftReleased;

    /// <summary>The mouse button was released after an armed drag.</summary>
    public event Action? ShiftDragEnded;

    /// <summary>Any left or right click anywhere, used to close the menu on outside clicks.</summary>
    public event Action<Native.POINT>? MouseDown;

    public bool Start()
    {
        using var ready = new ManualResetEventSlim();
        var ok = false;

        _thread = new Thread(() =>
        {
            _threadId = Native.GetCurrentThreadId();
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
            ok = _hook != IntPtr.Zero;
            ready.Set();
            if (!ok)
            {
                return;
            }

            while (Native.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
                // Low-level hooks are delivered through this thread's message loop.
            }

            Native.UnhookWindowsHookEx(_hook);
        })
        {
            IsBackground = true,
            Name = "Halora mouse hook",
        };

        _thread.Start();
        ready.Wait();
        return ok;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            var pt = info.pt;

            switch (msg)
            {
                case Native.WM_LBUTTONDOWN:
                    _buttonDown = true;
                    _armed = false;
                    _downAt = pt;
                    Post(() => MouseDown?.Invoke(pt));
                    break;

                case Native.WM_RBUTTONDOWN:
                    Post(() => MouseDown?.Invoke(pt));
                    break;

                case Native.WM_MOUSEMOVE when _buttonDown:
                    OnMoveWhileDown(pt);
                    break;

                case Native.WM_LBUTTONUP:
                    _buttonDown = false;
                    if (_armed)
                    {
                        _armed = false;
                        Post(() => ShiftDragEnded?.Invoke());
                    }

                    break;
            }
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void OnMoveWhileDown(Native.POINT pt)
    {
        var shift = Native.IsShiftDown();

        if (!_armed)
        {
            var dx = pt.X - _downAt.X;
            var dy = pt.Y - _downAt.Y;
            if (shift && (dx * dx) + (dy * dy) >= StartDistance * StartDistance)
            {
                _armed = true;
                Post(() => ShiftDragStarted?.Invoke(pt));
            }

            return;
        }

        if (!shift)
        {
            _armed = false;
            Post(() => ShiftReleased?.Invoke());
            return;
        }

        // Coalesce moves: only one pending UI update at a time, always with the newest point.
        _latest = pt;
        if (Interlocked.Exchange(ref _movePending, 1) == 0)
        {
            Post(() =>
            {
                Interlocked.Exchange(ref _movePending, 0);
                ShiftDragMoved?.Invoke(_latest);
            });
        }
    }

    private void Post(Action action) => _ui.BeginInvoke(DispatcherPriority.Input, action);

    public void Dispose()
    {
        if (_thread is { IsAlive: true })
        {
            Native.PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(1));
        }
    }
}
