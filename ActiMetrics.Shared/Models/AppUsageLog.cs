namespace ActiMetrics.Shared.Models
{
    public class AppUsageLog
    {
        public int Id { get; set; }
        public string WorkerId { get; set; } = string.Empty;
        public string WorkerUserName { get; set; } = string.Empty;
        public string IntervalStart { get; set; } = string.Empty;
        public string IntervalEnd { get; set; } = string.Empty;
        public string Apps { get; set; } = string.Empty; // JSON: [{"app":"chrome.exe","seconds":142.5}]
        public int ActiveSeconds { get; set; }
        public int IdleSeconds { get; set; }
        public int MouseClicks { get; set; }
        public int Keystrokes { get; set; }
        public bool Synced { get; set; }
    }
}
