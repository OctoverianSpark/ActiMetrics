using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace ActiMetrics.Service.Services
{
    public sealed class InputTrackerService : IDisposable
    {
        private readonly ILogger<InputTrackerService> _logger;

        // Contadores (thread-safe)
        private int _activeTicks;
        private int _idleTicks;
        private int _clicks;
        private int _keystrokes;

        // Hooks
        private IntPtr _mouseHookId   = IntPtr.Zero;
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private CancellationTokenSource? _cts;
        private Thread? _hookThread;

        // Mantener referencias para que el GC no los recoja
        private readonly HookProc _mouseProcDelegate;
        private readonly HookProc _keyboardProcDelegate;

        // ── Win32 ────────────────────────────────────────────────────────────

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_MOUSE_LL    = 14;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_KEYDOWN     = 0x0100;
        private const int WM_SYSKEYDOWN  = 0x0104;
        private const uint PM_REMOVE     = 0x0001;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX, ptY; }

        // ── Constructor ──────────────────────────────────────────────────────

        public InputTrackerService(ILogger<InputTrackerService> logger)
        {
            _logger               = logger;
            _mouseProcDelegate    = MouseProc;
            _keyboardProcDelegate = KeyboardProc;
        }

        // ── Ciclo de vida ────────────────────────────────────────────────────

        public void Start()
        {
            _cts        = new CancellationTokenSource();
            _hookThread = new Thread(() => HookLoop(_cts.Token))
            {
                Name         = "InputHookThread",
                IsBackground = true
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
        }

        private void HookLoop(CancellationToken ct)
        {
            _mouseHookId    = SetWindowsHookEx(WH_MOUSE_LL,    _mouseProcDelegate,    IntPtr.Zero, 0);
            _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProcDelegate, IntPtr.Zero, 0);

            if (_mouseHookId    == IntPtr.Zero) _logger.LogWarning("[Input] Mouse hook no instalado");
            if (_keyboardHookId == IntPtr.Zero) _logger.LogWarning("[Input] Keyboard hook no instalado");

            while (!ct.IsCancellationRequested)
            {
                while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                Thread.Sleep(10);
            }

            if (_mouseHookId    != IntPtr.Zero) UnhookWindowsHookEx(_mouseHookId);
            if (_keyboardHookId != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHookId);
        }

        // ── Hooks ────────────────────────────────────────────────────────────

        private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var msg = (uint)wParam;
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                    Interlocked.Increment(ref _clicks);
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var msg = (uint)wParam;
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    Interlocked.Increment(ref _keystrokes);
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        // ── API pública ──────────────────────────────────────────────────────

        // Llamar cada segundo desde ScreenWorker
        public void Tick()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            GetLastInputInfo(ref info);

            // uint aritmético maneja el overflow de TickCount (~49 días)
            var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);

            if (idleMs < 30_000)
                Interlocked.Increment(ref _activeTicks);
            else
                Interlocked.Increment(ref _idleTicks);
        }

        // Retorna y resetea los contadores del intervalo actual.
        // Llamar justo antes de que AppTrackerService haga flush.
        public (int ActiveSeconds, int IdleSeconds, int MouseClicks, int Keystrokes) GetAndResetCounts()
        {
            return (
                Interlocked.Exchange(ref _activeTicks, 0),
                Interlocked.Exchange(ref _idleTicks,   0),
                Interlocked.Exchange(ref _clicks,      0),
                Interlocked.Exchange(ref _keystrokes,  0)
            );
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _hookThread?.Join(2000);
            _cts?.Dispose();
        }
    }
}
