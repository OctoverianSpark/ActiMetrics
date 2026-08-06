namespace ActiMetrics.Shared.Models
{
    public class TicketRecord
    {
        public int Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public bool Synced { get; set; }
    }
}
