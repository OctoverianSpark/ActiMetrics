namespace Tracer.Shared.Models
{
    public class StateLog
    {
        public int Id { get; set; }
        public string WorkerId { get; set; }
        public StateCategory Category { get; set; }
        public WorkState State { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Synced { get; set; }
    }
}