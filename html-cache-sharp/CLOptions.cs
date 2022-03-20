using CommandLine;
using CommandLine.Text;

namespace HtmlCache
{
    internal class CLOptions
    {
        [Option(shortName: 'v', longName: "verbose", Required = false, HelpText = "Verbose output", Default = false)]
        public bool Verbose { get; set; }

        [Option(shortName: 'r', longName: "chrome-revision", Required = false, HelpText = "Chrome revision", Default = null)]
        public string? ChromeRevision { get; set; }

        [Option(shortName: 'с', longName: "config-path", Required = false, HelpText = "Path to config", Default = ".")]
        public string? ConfigPath { get; set; }

        [Usage(ApplicationAlias = "bs-deploy")]
        public static IEnumerable<Example> Examples => new List<Example>() {
            new Example("Cache html pages by config in `/var/www/htdocs/skin-expert`", new CLOptions { ConfigPath = "/var/www/htdocs/skin-expert" })
        };
    }
}
