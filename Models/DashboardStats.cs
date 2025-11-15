namespace CmsTools.Models
{
    public sealed class DashboardStats
    {
        public int ActiveConnections { get; set; }
        public int EnabledTables { get; set; }
        public int AuditLast24h { get; set; }
    }
}
