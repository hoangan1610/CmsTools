namespace CmsTools.Models
{
    public sealed class HomeDashboardViewModel
    {
        public bool IsAdmin { get; set; }

        public int TotalConnections { get; set; }
        public int ActiveConnections { get; set; }

        public int TotalTables { get; set; }
        public int EnabledTables { get; set; }

        public int AuditLast24h { get; set; }

        // Bảng đầu tiên user có quyền xem (nếu có)
        public int? FirstTableIdForUser { get; set; }
    }
}
