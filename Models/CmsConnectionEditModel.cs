namespace CmsTools.Models
{
    public sealed class CmsConnectionEditModel
    {
        public int? Id { get; set; }          // null = tạo mới, >0 = sửa
        public string Name { get; set; } = string.Empty;
        public string Provider { get; set; } = "mssql";
        public string ConnString { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
