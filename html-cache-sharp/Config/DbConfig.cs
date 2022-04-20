namespace HtmlCache.Config
{
    internal class DbConfig
    {
        public string Host { get; set; }
        public int? Port { get; set; }
        public string User { get; set; }
        public string Passwd { get; set; }
        public string Db { get; set; }
        public string? Collection { get; set; }
        public string? Table { get; set; }
    }
}
