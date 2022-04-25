using HtmlCache.Config;
using HtmlCache.DB;
using log4net;
using Newtonsoft.Json;
using PuppeteerSharp;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HtmlCache.Process
{
    internal class Renderer
    {
        private static readonly ILog log = LogManager.GetLogger("Renderer");

        public int Passed { get; internal set; }
        public int Total { get; internal set; }

        public List<Url> UrlSet { get; internal set; }

        internal IDB Db;
        public Renderer(List<Url> urlset)
        {
            string dbDriver = AppConfig.Instance.DbDriver;

            Db = dbDriver.ToLower() switch
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
            _ = await BrowserLoader.OpenNewPageAsync(); //open default page

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

            log.Info($"Detected url groups:\n{JsonConvert.SerializeObject(detectedUrls, Formatting.Indented)}");

            log.Info("Start collect cache for urls by detected groups");

            _ = await BrowserLoader.OpenNewPageAsync(); //open default page

            TimeSpan totalTime = new();

            foreach (var groupedUrl in groupedUrlSet)
            {
                var pageType = groupedUrl.Key;
                var urlList = groupedUrl.Value;

                if (urlList.Count == 0)
                {
                    log.Warn($"Url list for type `{pageType}` is empty - SKIP");
                    continue;
                }

                log.Info($"Collect cache for type `{pageType}`");

                totalTime += await IterateUrlsAsync(urlList, totalTime);
            }
        }

        internal async Task<TimeSpan> IterateUrlsAsync(List<Url>? urlSet = null, TimeSpan? offsetTime = null)
        {
            bool multithread = AppConfig.Instance.Multithread;
            bool verbose = AppConfig.Instance.Verbose;

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

                int maxThreads = AppConfig.Instance.MaxThreads;

                if (maxThreads <= 0 || maxThreads > Environment.ProcessorCount)
                {
                    maxThreads = Environment.ProcessorCount;
                }

                log.Info($"Maximum degree of parallelizm (max threads count) is {maxThreads}");

                Queue<Page> pageQueue = new();

                for (int i = 0; i < maxThreads * 2; i++)
                {
                    pageQueue.Enqueue(await BrowserLoader.OpenNewPageAsync());
                }

                ParallelOptions pOpts = new()
                {
                    MaxDegreeOfParallelism = maxThreads
                };

                await Parallel.ForEachAsync(urlSet, pOpts, async (url, state) =>
                {
                    current++;

                    int cursor = current;

                    TimeSpan pageTiming = new();

                    string currentPageType = pageTypes
                        .Where(pt => Regex.IsMatch(url.Uri, pt.Regex, RegexOptions.IgnoreCase))
                        .Select(pt => pt.Type)
                        .FirstOrDefault("undefined");

                    log.Info($"[{cursor} of {totalUrls}: {currentPageType}] Process page {url.Uri}");

                    Page page = pageQueue.Dequeue();

                    try
                    {
                        pageTiming = await CollectCacheAsync(
                            url: url,
                            pageType: currentPageType,
                            page: page,
                            cursor: cursor,
                            totalUrls: totalUrls
                        );

                        totalTime = DateTime.Now - timeStart;
                    }
                    catch (Exception ex)
                    {
                        log.Error($"[{cursor} of {totalUrls}: {currentPageType}] Error occured: " + ex.ToString());
                    }
                    finally
                    {
                        await page.GoToAsync("about:blank");

                        pageQueue.Enqueue(page);
                    }

                    log.Info($"[{cursor} of {totalUrls}: {currentPageType}] Render end in {pageTiming} / {totalTime}{(offsetTime is null ? "" : $" ({offsetTime + totalTime})")}");
                });

                foreach (var page in pageQueue)
                {
                    await page.CloseAsync();
                }

                pageQueue.Clear();
            }
            else
            {
                log.Info("Start iterating over url list in sequential mode");

                using var page = await BrowserLoader.OpenNewPageAsync();

                foreach (var url in urlSet)
                {
                    current++;

                    TimeSpan pageTiming = new();

                    string currentPageType = pageTypes
                        .Where(pt => Regex.IsMatch(url.Uri, pt.Regex, RegexOptions.IgnoreCase))
                        .Select(pt => pt.Type)
                        .FirstOrDefault("undefined");

                    log.Info($"[{current} of {totalUrls}: {currentPageType}] Process page {url.Uri}...");

                    try
                    {
                        pageTiming = await CollectCacheAsync(
                            url: url,
                            pageType: currentPageType,
                            page: page,
                            cursor: current,
                            totalUrls: totalUrls
                        );

                        totalTime += pageTiming;
                    }
                    catch (Exception ex)
                    {
                        log.Error($"[{current} of {totalUrls}: {currentPageType}] Error occured: " + ex.ToString());
                    }

                    log.Info($"[{current} of {totalUrls}: {currentPageType}] Render end in {pageTiming} / {totalTime}{(offsetTime is null ? "" : $" ({offsetTime + totalTime})")}");
                }

                await page.CloseAsync();
            }

            return totalTime;
        }

        internal async Task<TimeSpan> CollectCacheAsync(Url url, string pageType = "undefined", Page? page = null, Browser? browser = null, int cursor = 0, int? totalUrls = null)
        {
            if (totalUrls is null)
            {
                totalUrls = Total;
            }

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

            log.Info($"[{cursor} of {totalUrls}: {pageType}] Page url hash: {urlHash}");

            var render = Db.FindByHash(urlHash);

            if (render != null)
            {
                log.Info($"[{cursor} of {totalUrls}: {pageType}] Found render data: #{render.ID} - {render.RenderDate}");

                if (render.LastmodDate == url.LastMod)
                {
                    log.Warn($"[{cursor} of {totalUrls}: {pageType}] Render lastmod date not changed - SKIP");

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
                    log.Warn($"[{cursor} of {totalUrls}: {pageType}] NOINDEX meta attribute detected - SKIP");

                    Passed++;

                    return DateTime.Now - pageTimeStart;
                }

                if (verbose)
                {
                    log.Debug($"[{cursor} of {totalUrls}: {pageType}] Wait for opened");
                }

                await page.WaitForFunctionAsync("(pageType) => AppCore.Pages.of(pageType).isOpened()", pageType);

                if (verbose)
                {
                    log.Debug($"[{cursor} of {totalUrls}: {pageType}] Wait for loaded");
                }

                await page.WaitForFunctionAsync("(pageType) => AppCore.Pages.of(pageType).isLoaded()", pageType);

                string htmlContent = await page.GetContentAsync();

                htmlContent = htmlContent.Replace("<meta name=\"fragment\" content=\"!\">", "");

                Regex scriptRegex = new(@"<script.+src=""\/theme\/frontend\/app\/build\/main\.js\?ver=\d+""><\/script>");
                htmlContent = scriptRegex.Replace(htmlContent, "");

                byte[] content = Encoding.UTF8.GetBytes(htmlContent);

                string contentHash = Convert.ToHexString(MD5.HashData(content)).ToLower();

                log.Info($"[{cursor} of {totalUrls}: {pageType}] Computed content hash: {contentHash}");

                if (render != null && render.ContentHash == contentHash)
                {
                    log.Warn($"[{cursor} of {totalUrls}: {pageType}] Render content hash not changed - SKIP");

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

                log.Error($"[{cursor} of {totalUrls}: {pageType}] Fail. Reason: {e.Message}");
                log.Error($"[{cursor} of {totalUrls}: {pageType}] State: {JsonConvert.SerializeObject(pageState, Formatting.Indented)}");
                log.Error($"[{cursor} of {totalUrls}: {pageType}] Saving fail screen");

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

                    log.Error($"[{cursor} of {totalUrls}: {pageType}] Fail screen saved to `{failScreenPath}`");
                }
                catch (Exception)
                {
                    log.Error($"[{cursor} of {totalUrls}: {pageType}] Error occured while capturing screenshot");

                    throw;
                }
            }

            return DateTime.Now - pageTimeStart;
        }
    }
}
