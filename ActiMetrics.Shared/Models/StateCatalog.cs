namespace ActiMetrics.Shared.Models
{
    public class StateCategoryCatalogItem
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Sort_Order { get; set; }
    }

    public class StateCatalogItem
    {
        public int Id { get; set; }
        public int Code { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Category_Id { get; set; }
        public int Sort_Order { get; set; }
        public StateCategoryCatalogItem? Category { get; set; }
    }

    public class GroupStateVisibilityItem
    {
        public int Id { get; set; }
        public int Group_Id { get; set; }
        public int Code { get; set; }
    }
}
