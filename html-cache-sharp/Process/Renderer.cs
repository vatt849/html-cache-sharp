using HtmlCache.Config;
using HtmlCache.DB;
using Newtonsoft.Json;
using PuppeteerSharp;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HtmlCache.Process
{
    internal class Renderer
    {
        public int Passed { get; internal set; }
        public int Total { get; internal set; }

        public List<Url> UrlSet { get; internal set; }

        internal IDB Db;
        public Renderer(List<Url> urlset)
        {
            string dbDriver = AppConfig.Instance.DbDriver;

            Db = dbDriver switch
            {
                "redis" => new Redis(),
                "mongodb" => new Mongo(),
                _ => throw new Exception($"DB driver not recognized by identifier `{dbDriver}`"),
            };

            UrlSet = urlset;
            Total = UrlSet.Count;
        }

        public async Task CollectAsync()
        {
            bool verbose = AppConfig.Instance.Verbose;
            var pageTypes = AppConfig.Instance.PageTypes ?? new();

            using var browser = await BrowserLoader.GetBrowser();

            int progress = 0;

            using var page = await browser.NewPageAsync();

            if (verbose)
            {
                page.Load += new EventHandler((sender, e) => Console.WriteLine($">>>> {((Page)sender).Url} loaded!"));
            }

            foreach (var url in UrlSet)
            {
                progress++;

                string currentPageType = pageTypes
                    .Where(pt => Regex.IsMatch(url.Uri, pt.Regex, RegexOptions.IgnoreCase))
                    .Select(pt => pt.Type)
                    .FirstOrDefault("undefined");

                Console.WriteLine($">> [{progress} of {Total}][{currentPageType}] Process page {url.Uri}...");

                string urlHash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(url.Uri))).ToLower();

                Console.WriteLine($">>>> Page url hash: {urlHash}");

                var render = Db.FindByHash(urlHash);

                if (render != null)
                {
                    Console.WriteLine($">>>> Found render data: #{render.ID} - {render.RenderDate}");

                    if (render.LastmodDate == url.LastMod)
                    {
                        Console.WriteLine($">>>> Render lastmod date not changed - SKIP");

                        Passed++;

                        continue;
                    }
                }

                try
                {
                    string prerenderUrl = url.Uri.Contains('?') ? $"{url.Uri}&is_prerender=1" : $"{url.Uri}?is_prerender=1";

                    await page.GoToAsync(prerenderUrl, new NavigationOptions
                    {
                        Timeout = AppConfig.Instance.Timeout ?? 60000,
                        WaitUntil = new[] { WaitUntilNavigation.Load }
                    });

                    if (verbose)
                    {
                        Console.WriteLine($">>>> Wait for opened...");
                    }

                    await page.WaitForFunctionAsync("(pageType) => AppCore.Pages.of(pageType).isOpened()", currentPageType);

                    if (verbose)
                    {
                        Console.WriteLine($">>>> Wait for loaded...");
                    }

                    await page.WaitForFunctionAsync("(pageType) => AppCore.Pages.of(pageType).isLoaded()", currentPageType);

                    string htmlContent = await page.GetContentAsync();

                    htmlContent = htmlContent.Replace("<meta name=\"fragment\" content=\"!\">", "");

                    Regex scriptRegex = new(@"<script.+src=""\/theme\/frontend\/app\/build\/main\.js\?ver=\d+""><\/script>");
                    htmlContent = scriptRegex.Replace(htmlContent, "");

                    byte[] content = Encoding.UTF8.GetBytes(htmlContent);

                    string contentHash = Convert.ToHexString(MD5.HashData(content)).ToLower();

                    Console.WriteLine($">>>> Computed content hash: {contentHash}");

                    if (render != null && render.ContentHash == contentHash)
                    {
                        Console.WriteLine($">>>> Render content hash not changed - SKIP");

                        Passed++;

                        continue;
                    }

                    if (render == null)
                    {
                        render = new();

                        render.Hash = urlHash;
                        render.Url = url.Uri;
                    }

                    render.RenderDate = DateTime.Now;
                    render.LastmodDate = url.LastMod;
                    render.ContentHash = contentHash;
                    render.Content = content;

                    if (!Db.Save(render))
                    {
                        throw new Exception("DB saving error");
                    }

                    Passed++;
                }
                catch (Exception e)
                {
                    var pageState = await page.EvaluateFunctionAsync("(pageType) => AppCore.Pages.of(pageType).getState()", currentPageType);

                    Console.WriteLine($">>>> Fail. Reason: {e.Message}");
                    Console.WriteLine($">>>> State: {JsonConvert.SerializeObject(pageState, Formatting.Indented)}");
                    Console.WriteLine(">>>> Saving fail screen...");

                    try
                    {
                        string failScreenPath = $"./pages/fail-{urlHash}.png";

                        await page.ScreenshotAsync(failScreenPath, new ScreenshotOptions
                        {
                            FullPage = true,
                        });

                        Console.WriteLine($">>>> Fail screen saved to `{failScreenPath}`");
                    }
                    catch (Exception)
                    {
                        Console.Write(">> Error occured while capturing screenshot");

                        throw;
                    }
                }
            }
        }
    }
}
