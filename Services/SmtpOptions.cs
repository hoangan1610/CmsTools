namespace CmsTools.Services
{
    public sealed class SmtpOptions
    {
        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;

        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public string DefaultFrom { get; set; } = string.Empty;
        public string DefaultTo { get; set; } = string.Empty;
    }
}
