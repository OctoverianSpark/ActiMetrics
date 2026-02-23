using System.Runtime.InteropServices;

namespace Tracer.Service.Services
{
    public class ActivityService
    {
        private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        public TimeSpan GetIdleTime()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);
            GetLastInputInfo(ref info);

            uint idleMs = (uint)Environment.TickCount - info.dwTime;
            return TimeSpan.FromMilliseconds(idleMs);
        }

        public bool IsIdle() => GetIdleTime() >= IdleThreshold;
    }
}