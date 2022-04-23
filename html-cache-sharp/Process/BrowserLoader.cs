using HtmlCache.Config;
using log4net;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;
using System.Net;

namespace HtmlCache.Process
{
    internal static class BrowserLoader
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(BrowserLoader));
        public async static Task CheckRevisionAsync()
        {
            string? manualRevision = AppConfig.Instance.BrowserRevision;

            string actualRevision;

            if (manualRevision == null)
            {
                log.Info("Retrieving info about actual revision of Chrome...");

                actualRevision = await GetActualRevisionAsync();
                //string actualRevision = BrowserFetcher.DefaultChromiumRevision;

                log.Info($"Actual Chrome revision: {actualRevision}");
            }
            else
            {
                actualRevision = manualRevision;

                log.Info($"Chrome revision set manually to {actualRevision}");
            }

            var browserFetcher = new BrowserFetcher();

            log.Info("Check installed revision");

            var localRevision = browserFetcher.RevisionInfo(actualRevision);

            if (localRevision.Local)
            {
                AppConfig.Instance.BrowserRevision = localRevision.Revision;

                log.Info("Local revision is in actual version");
                return;
            }

            var localRevisions = browserFetcher.LocalRevisions();

            if (localRevisions.Any())
            {
                log.Info("Local revision is not in actual version - need to remove old revisions and download new one");

                foreach (string revision in browserFetcher.LocalRevisions())
                {
                    browserFetcher.Remove(revision);
                }
            }
            else
            {
                log.Info("Installed revisions of Chrome not found - need to download new one");
            }

            log.Info("Start downloading Chrome");

            bool canDownload = await browserFetcher.CanDownloadAsync(actualRevision);

            if (!canDownload)
            {
                throw new Exception($"Actual revision of Chrome ({actualRevision}) is not available to download!");
            }

            DateTime _startedAt = default;

            browserFetcher.DownloadProgressChanged += (object sender, DownloadProgressChangedEventArgs e) =>
            {
                var bytesPerSecond = 0;

                if (_startedAt == default)
                {
                    _startedAt = DateTime.Now;
                }

                TimeSpan timeSpan = DateTime.Now - _startedAt;
                if (timeSpan.TotalSeconds > 0)
                {
                    bytesPerSecond = (int)Math.Round(e.BytesReceived / timeSpan.TotalSeconds, 0);
                }

                Console.Write($"\r>> Download progress: {e.ProgressPercentage}% [{bytesPerSecond / 1024} KB/s]     ");
            };

            await browserFetcher.DownloadAsync(actualRevision);

            localRevision = browserFetcher.RevisionInfo(actualRevision);

            if (!localRevision.Local)
            {
                Console.WriteLine();
                throw new Exception($"Actual revision of Chrome ({actualRevision}) does not downloaded properly!");
            }

            AppConfig.Instance.BrowserRevision = localRevision.Revision;

            Console.WriteLine();
            log.Info("Chrome downloading complete!");
        }

        public static async Task<Dictionary<Platform, string>> GetActualRevisionsListAsync()
        {
            Dictionary<Platform, string> revisions = new()
            {
                { Platform.Win32, BrowserFetcher.DefaultChromiumRevision },
                { Platform.Win64, BrowserFetcher.DefaultChromiumRevision },
                { Platform.Linux, BrowserFetcher.DefaultChromiumRevision },
                { Platform.MacOS, BrowserFetcher.DefaultChromiumRevision },
                { Platform.Unknown, BrowserFetcher.DefaultChromiumRevision }
            };

            foreach (var revision in revisions)
            {
                revisions[revision.Key] = await GetActualPlatformRevisionAsync(revision.Key);
            }

            return revisions;
        }

        public static async Task<string> GetActualRevisionAsync()
        {
            var browserFetcher = new BrowserFetcher();

            return await GetActualPlatformRevisionAsync(browserFetcher.Platform);
        }

        internal static async Task<string> GetActualPlatformRevisionAsync(Platform platform)
        {
            string platformName = platform switch
            {
                Platform.Win32 => "Win32",
                Platform.Win64 => "Windows",
                Platform.Linux => "Linux",
                Platform.MacOS => "Mac",
                _ => ""
            };

            if (string.IsNullOrEmpty(platformName))
            {
                return BrowserFetcher.DefaultChromiumRevision;
            }

            HttpClient client = new();

            var revDataResp = await client.GetAsync(new Uri($"https://chromiumdash.appspot.com/fetch_releases?channel=Stable&num=1&platform={platformName}"));

            JArray revDataList = JArray.Parse(await revDataResp.Content.ReadAsStringAsync());

            JObject revData = (JObject)revDataList[0] ?? new();

            return (string)revData["chromium_main_branch_position"] ?? "";
        }

        public static Task<Browser> GetBrowserAsync()
        {
            var browserFetcher = new BrowserFetcher();
            var installedRevision = browserFetcher.RevisionInfo(AppConfig.Instance.BrowserRevision);

            return Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                ExecutablePath = installedRevision.ExecutablePath,
                DefaultViewport = new()
                {
                    Width = 1920,
                    Height = 1080
                }
            });
        }

        public static async Task LaunchBrowserAsync()
        {
            AppConfig.Instance.Browser = await GetBrowserAsync();
        }

        public static async Task<Page> OpenNewPageAsync()
        {
            var browser = AppConfig.Instance.Browser;
            if (browser is null)
            {
                throw new Exception("Browser not launched");
            }

            var page = await browser.NewPageAsync();

            if (AppConfig.Instance.Verbose)
            {
                page.Load += new EventHandler((sender, e) =>
                {
                    var page = (Page?)sender;

                    if (page != null)
                    {
                        log.Debug($"{page.Url} loaded!");
                    }
                });
            }

            return page;
        }

        public static async Task CloseBrowserAsync()
        {
            if (AppConfig.Instance.Browser != null)
            {
                await AppConfig.Instance.Browser.CloseAsync();
            }
        }
    }
}
