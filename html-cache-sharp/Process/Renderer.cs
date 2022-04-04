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

            using var browser = AppConfig.Instance.Browser;
            if (browser == null)
            {
                throw new Exception("Browser not launched");
            }
            //using var browser = await BrowserLoader.GetBrowser();

            int progress = 0;

            using var page = await browser.NewPageAsync();

            if (verbose)
            {
                page.Load += new EventHandler((sender, e) => Console.WriteLine($">>>> {((Page)sender).Url} loaded!"));
            }

            TimeSpan totalTime = new();

            foreach (var url in UrlSet)
            {
                progress++;

                TimeSpan pageTiming = new();

                string currentPageType = pageTypes
                    .Where(pt => Regex.IsMatch(url.Uri, pt.Regex, RegexOptions.IgnoreCase))
                    .Select(pt => pt.Type)
                    .FirstOrDefault("undefined");

                Console.WriteLine($">> [{progress} of {Total}][{currentPageType}] Process page {url.Uri}...");

                try
                {
                    pageTiming = await CollectCacheAsync(url, currentPageType, page);

                    totalTime += pageTiming;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[!] Error occured: " + ex.ToString());
                }

                Console.WriteLine($">>>> Render end in {pageTiming} / {totalTime}");
            }
        }

        public async Task CollectGroupedAsync()
        {
            bool verbose = AppConfig.Instance.Verbose;
            var pageTypes = AppConfig.Instance.PageTypes ?? new();

            Dictionary<string, List<Url>> groupedUrlSet = new()
            {
                { "undefined", new() }
            };

            pageTypes.ForEach(type => groupedUrlSet[type.Type] = new List<Url>());

            UrlSet.ForEach(url =>
            {
                string urlPageType = pageTypes
                    .Where(pt => Regex.IsMatch(url.Uri, pt.Regex, RegexOptions.IgnoreCase))
                    .Select(pt => pt.Type)
                    .FirstOrDefault("undefined");

                groupedUrlSet[urlPageType].Add(url);
            });

            Console.WriteLine(">> Detected url groups:");
            foreach (var groupedUrl in groupedUrlSet)
            {
                var urlType = groupedUrl.Key;
                var urlList = groupedUrl.Value;

                Console.WriteLine($">>>> {urlType}: {urlList.Count} urls");
            }

            Console.WriteLine(">> Start collect cache for urls by detected groups");


            using var browser = AppConfig.Instance.Browser;
            if (browser == null)
            {
                throw new Exception("Browser not launched");
            }
            //using var browser = await BrowserLoader.GetBrowser();
            _ = await browser.NewPageAsync(); //open default page

            foreach (var groupedUrl in groupedUrlSet)
            {
                var pageType = groupedUrl.Key;
                var urlList = groupedUrl.Value;

                Console.WriteLine($">> Collect cache for type `{pageType}`:");

                using var page = await browser.NewPageAsync();

                if (verbose)
                {
                    page.Load += new EventHandler((sender, e) => Console.WriteLine($">>>> {((Page)sender).Url} loaded!"));
                }

                TimeSpan totalTime = new();

                int progress = 0;

                foreach (var url in urlList)
                {
                    progress++;

                    TimeSpan pageTiming = new();

                    Console.WriteLine($">> [{progress} of {Total}][{pageType}] Process page {url.Uri}...");

                    try
                    {
                        pageTiming = await CollectCacheAsync(url, pageType, page);

                        totalTime += pageTiming;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[!] Error occured: " + ex.ToString());
                    }

                    Console.WriteLine($">>>> Render end in {pageTiming} / {totalTime}");
                }

                await page.CloseAsync();
            }
        }

        internal async Task<TimeSpan> CollectCacheAsync(Url url, string pageType = "undefined", Page? page = null, Browser? browser = null)
        {
            bool verbose = AppConfig.Instance.Verbose;

            DateTime pageTimeStart = DateTime.Now;

            if (page == null)
            {
                if (browser == null)
                {
                    throw new ArgumentNullException(nameof(browser));
                }

                page = await browser.NewPageAsync();
            }

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

                    return DateTime.Now - pageTimeStart;
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

                await page.WaitForFunctionAsync("(pageType) => AppCore.Pages.of(pageType).isOpened()", pageType);

                if (verbose)
                {
                    Console.WriteLine($">>>> Wait for loaded...");
                }

                await page.WaitForFunctionAsync("(pageType) => AppCore.Pages.of(pageType).isLoaded()", pageType);

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

                    return DateTime.Now - pageTimeStart;
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
                var pageState = await page.EvaluateFunctionAsync("(pageType) => AppCore.Pages.of(pageType).getState()", pageType);

                Console.WriteLine($">>>> Fail. Reason: {e.Message}");
                Console.WriteLine($">>>> State: {JsonConvert.SerializeObject(pageState, Formatting.Indented)}");
                Console.WriteLine(">>>> Saving fail screen...");

                try
                {
                    if (!Directory.Exists("./pages"))
                    {
                        Directory.CreateDirectory("./pages");
                    }

                    string failScreenPath = $"./pages/fail-{urlHash}.png";

                    await page.ScreenshotAsync(failScreenPath, new ScreenshotOptions
                    {
                        FullPage = true,
                    });

                    Console.WriteLine($">>>> Fail screen saved to `{failScreenPath}`");
                }
                catch (Exception)
                {
                    Console.WriteLine(">> Error occured while capturing screenshot");

                    throw;
                }
            }

            return DateTime.Now - pageTimeStart;
        }
    }
}
