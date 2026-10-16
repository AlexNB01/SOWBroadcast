using System.Runtime.InteropServices;
using SowBroadcast.Core.Control;

namespace SowBroadcast.App.Services;

/// <summary>
/// Hotkeys that work while another program (OBS, the game) has the focus: Windows' RegisterHotKey on a thread of its own with
/// a message loop. <see cref="Pressed"/> is raised on that thread; the caller moves to the UI thread.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmQuit = 0x0012;
    private const uint ModNoRepeat = 0x4000;   // holding the keys down must not fire the command over and over
    private const uint PmNoRemove = 0;

    private Thread? _thread;
    private uint _threadId;

    /// <summary>Raised with the command id when its hotkey is pressed.</summary>
    public event Action<string>? Pressed;

    /// <summary>
    /// Registers exactly these hotkeys (replacing the earlier ones). Returns the commands whose key combination Windows would
    /// not give us, usually because another program already uses it.
    /// </summary>
    public IReadOnlyList<string> Apply(IReadOnlyList<(string Id, HotkeyGesture Gesture)> bindings)
    {
        Stop();
        if (!OperatingSystem.IsWindows() || bindings.Count == 0) return Array.Empty<string>();

        var failed = new List<string>();
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Loop(bindings, failed, ready)) { IsBackground = true, Name = "Global hotkeys" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        return failed;
    }

    private void Loop(IReadOnlyList<(string Id, HotkeyGesture Gesture)> bindings, List<string> failed, ManualResetEventSlim ready)
    {
        // Make the thread's message queue exist before anyone can post to it.
        PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
        _threadId = GetCurrentThreadId();

        var registered = new Dictionary<int, string>();
        var next = 1;
        foreach (var (id, gesture) in bindings)
        {
            if (RegisterHotKey(IntPtr.Zero, next, (uint)gesture.Modifiers | ModNoRepeat, (uint)gesture.VirtualKey)) registered[next++] = id;
            else failed.Add(id);
        }
        ready.Set();

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.Message == WmHotkey && registered.TryGetValue((int)message.WParam, out var command))
            {
                try { Pressed?.Invoke(command); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "A hotkey handler failed"); }
            }
        }

        foreach (var key in registered.Keys) UnregisterHotKey(IntPtr.Zero, key);
    }

    private void Stop()
    {
        var thread = _thread;
        if (thread is null) return;
        PostThreadMessage(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg message, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Msg message, IntPtr hWnd, uint filterMin, uint filterMax, uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
