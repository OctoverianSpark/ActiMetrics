namespace Tracer.Shared.Models
{
    // Tracer.Shared/Models/StateLog.cs
    public class StateLog
    {
        public int Id { get; set; }
        public string WorkerId { get; set; }
        public StateCategory Category { get; set; }
        public WorkState State { get; set; }
        public StateType Type { get; set; }  // ← Auto o Manual
        public DateTime Timestamp { get; set; }
        public bool Synced { get; set; }
    }
}