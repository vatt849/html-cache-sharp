using HtmlCache.Config;
using HtmlCache.DB;
using log4net;
using Newtonsoft.Json;
using PuppeteerSharp;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HtmlCache.Process
{
    internal class Renderer
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Renderer));

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
                "mysql" => new Mysql(),
                _ => throw new Exception($"DB driver not recognized by identifier `{dbDriver}`"),
            };

            UrlSet = urlset;
            Total = UrlSet.Count;
        }

        public async Task CollectAsync()
        {
            await IterateUrlsAsync();
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

            var detectedUrls = new Dictionary<string, int>();
            foreach (var groupedUrl in groupedUrlSet)
            {
                var urlType = groupedUrl.Key;
                var urlList = groupedUrl.Value;

                detectedUrls[urlType] = urlList.Count;
            }

            log.InfoFormat("Detected url groups:\n{0}", detectedUrls);

            log.Info("Start collect cache for urls by detected groups");

            TimeSpan totalTime = new();

            foreach (var groupedUrl in groupedUrlSet)
            {
                var pageType = groupedUrl.Key;
                var urlList = groupedUrl.Value;

                log.Info($"Collect cache for type `{pageType}`");

                totalTime += await IterateUrlsAsync(urlList, totalTime);
            }
        }

        internal async Task<TimeSpan> IterateUrlsAsync(List<Url>? urlSet = null, TimeSpan? offsetTime = null)
        {
            bool multithread = AppConfig.Instance.Multithread;
            bool verbose = AppConfig.Instance.Verbose;

            _ = await BrowserLoader.OpenNewPageAsync(); //open default page

            if (urlSet == null)
            {
                urlSet = UrlSet;
            }

            var pageTypes = AppConfig.Instance.PageTypes ?? new();

            TimeSpan totalTime = new();
            int current = 0;

            int totalUrls = urlSet.Count;

            if (multithread)
            {
                log.Info("Start iterating over url list in parallel mode");

                var timeStart = DateTime.Now;

                ConcurrentDictionary<int, Page> pageBag = new();

                await Parallel.ForEachAsync(urlSet, async (url, state) =>
                {
                    int currentThread = Environment.CurrentManagedThreadId;

                    if (!pageBag.ContainsKey(currentThread))
                    {
                        pageBag[currentThread] = await BrowserLoader.OpenNewPageAsync();
                    }

                    var page = pageBag[currentThread];

                    current++;

                    int cursor = current;

                    TimeSpan pageTiming = new();

                    string currentPageType = pageTypes
                        .Where(pt => Regex.IsMatch(url.Uri, pt.Regex, RegexOptions.IgnoreCase))
                        .Select(pt => pt.Type)
                        .FirstOrDefault("undefined");

                    log.Info($"[{cursor} of {totalUrls}][{currentPageType}] Process page {url.Uri}");

                    try
                    {
                        pageTiming = await CollectCacheAsync(url, currentPageType, page);

                        totalTime = DateTime.Now - timeStart;
                    }
                    catch (Exception ex)
                    {
                        log.Error($"[{cursor} of {totalUrls}] Error occured: " + ex.ToString());
                    }

                    log.Info($"[{cursor} of {totalUrls}] Render end in {pageTiming} / {totalTime}{(offsetTime is null ? "" : $" ({offsetTime + totalTime})")}");
                });

                foreach (var page in pageBag)
                {
                    await page.Value.CloseAsync();
                }
            }
            else
            {
                log.Info("Start iterating over url list in sequential mode");

                var page = await BrowserLoader.OpenNewPageAsync();

                foreach (var url in urlSet)
                {
                    current++;

                    TimeSpan pageTiming = new();

                    string currentPageType = pageTypes
                        .Where(pt => Regex.IsMatch(url.Uri, pt.Regex, RegexOptions.IgnoreCase))
                        .Select(pt => pt.Type)
                        .FirstOrDefault("undefined");

                    log.Info($"[{current} of {totalUrls}][{currentPageType}] Process page {url.Uri}...");

                    try
                    {
                        pageTiming = await CollectCacheAsync(url, currentPageType, page);

                        totalTime += pageTiming;
                    }
                    catch (Exception ex)
                    {
                        log.Error($"[{current} of {totalUrls}] Error occured: " + ex.ToString());
                    }

                    log.Info($"[{current} of {totalUrls}] Render end in {pageTiming} / {totalTime}{(offsetTime is null ? "" : $" ({offsetTime + totalTime})")}");
                }

                await page.CloseAsync();
            }

            return totalTime;
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

                page = await BrowserLoader.OpenNewPageAsync();
            }

            string urlHash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(url.Uri))).ToLower();

            log.Info($"Page url hash: {urlHash}");

            var render = Db.FindByHash(urlHash);

            if (render != null)
            {
                log.Info($"Found render data: #{render.ID} - {render.RenderDate}");

                if (render.LastmodDate == url.LastMod)
                {
                    log.Warn($"Render lastmod date not changed - SKIP");

                    Passed++;

                    return DateTime.Now - pageTimeStart;
                }
            }

            try
            {
                string prerenderUrl = url.Uri.Contains('?') ? $"{url.Uri}&is_prerender=1" : $"{url.Uri}?is_prerender=1";

                var pageResponse = await page.GoToAsync(prerenderUrl, new NavigationOptions
                {
                    Timeout = AppConfig.Instance.Timeout ?? 60000,
                    WaitUntil = new[] { WaitUntilNavigation.Load }
                });

                if (pageResponse.Status == HttpStatusCode.NotFound)
                {
                    throw new Exception("Page not found");
                }

                string pageContent = await page.GetContentAsync();

                if (pageContent.Contains("<meta name=\"robots\" content=\"noindex\">"))
                {
                    log.Warn("NOINDEX meta attribute detected - SKIP");

                    Passed++;

                    return DateTime.Now - pageTimeStart;
                }

                if (verbose)
                {
                    log.Debug("Wait for opened");
                }

                await page.WaitForFunctionAsync("(pageType) => AppCore.Pages.of(pageType).isOpened()", pageType);

                if (verbose)
                {
                    log.Debug("Wait for loaded");
                }

                await page.WaitForFunctionAsync("(pageType) => AppCore.Pages.of(pageType).isLoaded()", pageType);

                string htmlContent = await page.GetContentAsync();

                htmlContent = htmlContent.Replace("<meta name=\"fragment\" content=\"!\">", "");

                Regex scriptRegex = new(@"<script.+src=""\/theme\/frontend\/app\/build\/main\.js\?ver=\d+""><\/script>");
                htmlContent = scriptRegex.Replace(htmlContent, "");

                byte[] content = Encoding.UTF8.GetBytes(htmlContent);

                string contentHash = Convert.ToHexString(MD5.HashData(content)).ToLower();

                log.Info($"Computed content hash: {contentHash}");

                if (render != null && render.ContentHash == contentHash)
                {
                    log.Warn("Render content hash not changed - SKIP");

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

                log.Error($"Fail. Reason: {e.Message}");
                log.Error($"State: {JsonConvert.SerializeObject(pageState, Formatting.Indented)}");
                log.Error("Saving fail screen");

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

                    log.Error($"Fail screen saved to `{failScreenPath}`");
                }
                catch (Exception)
                {
                    log.Error("Error occured while capturing screenshot");

                    throw;
                }
            }

            return DateTime.Now - pageTimeStart;
        }
    }
}
