using HtmlCache.Config;
using System.Xml.Linq;

namespace HtmlCache.Process
{
    internal static class Sitemap
    {
        public static List<Url> LoadUrls()
        {
            AppConfig config = AppConfig.Instance;

            string sitemapURL = config.BaseUri + "/sitemap.xml";

            if (config.Verbose)
            {
                Console.WriteLine($">> Loading sitemap from url: {sitemapURL}");
            }

            var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");

            XDocument xdoc = XDocument.Load(sitemapURL);

            var urlset = xdoc.Root?.Elements(ns + "url")
                .Where(u => u.Element(ns + "loc")?.Value != "")
                .Select(u => new
                {
                    uri = u.Element(ns + "loc")?.Value,
                    lastmod = u.Element(ns + "lastmod")?.Value,
                });

            List<Url> ret = new();

            if (urlset is null)
            {
                Console.WriteLine($">> Sitemap not loaded properly (urlset not found or empty)");

                return ret;
            }

            foreach (var url in urlset)
            {
                if (url is null || url.uri is null) continue;

                if (!DateTime.TryParse(url.lastmod, out DateTime LastMod))
                {
                    LastMod = DateTime.Now;
                }

                ret.Add(new Url
                {
                    LastMod = LastMod,
                    Uri = url.uri ?? "",
                });
            }

            return ret;
        }
    }
}
