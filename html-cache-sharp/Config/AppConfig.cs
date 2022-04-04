using PuppeteerSharp;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HtmlCache.Config
{
    internal class AppConfig
    {
        [YamlMember(Alias = "db_driver", ApplyNamingConventions = false)]
        public string DbDriver { get; set; }

        public DbConfig Db { get; set; }

        [YamlMember(Alias = "page_types", ApplyNamingConventions = false)]
        public List<PageType> PageTypes { get; set; }

        [YamlMember(Alias = "base_uri", ApplyNamingConventions = false)]
        public string BaseUri { get; set; }

        public bool Verbose { get; set; }

        public string? BrowserRevision { get; set; }

        public int? Timeout { get; set; }

        public Browser? Browser { get; set; }

        public static AppConfig Load(string configPath)
        {
            if (instance != null)
            {
                throw new Exception("Deploy config already loaded");
            }

            string yamlConfig = File.ReadAllText(configPath);

            var deserializer = new DeserializerBuilder()
                            .WithNamingConvention(CamelCaseNamingConvention.Instance)
                            //.IgnoreUnmatchedProperties()
                            .Build();

            instance = deserializer.Deserialize<AppConfig>(yamlConfig);

            return instance;
        }

        private static AppConfig? instance;

        public static AppConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    throw new Exception("App config not loaded");
                }

                return instance;
            }
        }
    }
}
