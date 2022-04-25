using CommandLine;
using CommandLine.Text;

namespace HtmlCache
{
    internal class CLOptions
    {
        [Option(shortName: 'v', longName: "verbose", Required = false, HelpText = "Verbose output", Default = false)]
        public bool Verbose { get; set; }

        [Option(shortName: 'g', longName: "group-urls", Required = false, HelpText = "Cache urls by group", Default = false)]
        public bool GroupUrls { get; set; }

        [Option(shortName: 'm', longName: "multithread", Required = false, HelpText = "Run in multithread mode", Default = false)]
        public bool Multithread { get; set; }

        [Option(shortName: 't', longName: "max-threads", Required = false, HelpText = "Max threads count (positive) to use in multithread mode (0 or any negative is for maximum available threads)", Default = 0)]
        public int MaxThreads { get; set; }

        [Option(shortName: 'r', longName: "chrome-revision", Required = false, HelpText = "Chrome revision", Default = "")]
        public string ChromeRevision { get; set; } = "";

        [Option(shortName: 'с', longName: "config-path", Required = false, HelpText = "Path to config", Default = ".")]
        public string? ConfigPath { get; set; }

        [Usage(ApplicationAlias = "html-cache-sharp")]
        public static IEnumerable<Example> Examples => new List<Example>() {
            new Example("Cache html pages by config in `/var/www/htdocs/skin-expert`", new CLOptions { ConfigPath = "/var/www/htdocs/skin-expert" }),
            new Example("Cache html pages in multithread mode", new CLOptions { Multithread = true }),
            new Example("Cache html pages in multithread mode with specified max threads", new CLOptions { Multithread = true, MaxThreads = 16 }),
            new Example("Cache html pages in grouped mode", new CLOptions { GroupUrls = true }),
            new Example("Cache html pages in verbose mode", new CLOptions { Verbose = true }),
            new Example("Cache html pages with specified Chrome browser revision", new CLOptions { ChromeRevision = "972766" }),
        };
    }
}
