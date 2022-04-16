using HtmlCache.Config;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;
using System.Net;

namespace HtmlCache.Process
{
    internal static class BrowserLoader
    {
        public async static Task CheckRevision()
        {
            string? manualRevision = AppConfig.Instance.BrowserRevision;

            string actualRevision;

            if (manualRevision == null)
            {
                Console.WriteLine(">> Retrieving info about actual revision of Chrome...");

                actualRevision = await GetActualRevisionAsync();
                //string actualRevision = BrowserFetcher.DefaultChromiumRevision;

                Console.WriteLine($">> Actual Chrome revision: {actualRevision}");
            }
            else
            {
                actualRevision = manualRevision;

                Console.WriteLine($">> Chrome revision set manually to {actualRevision}");
            }

            var browserFetcher = new BrowserFetcher();

            Console.WriteLine(">> Check installed revision...");

            var localRevision = browserFetcher.RevisionInfo(actualRevision);

            if (localRevision.Local)
            {
                AppConfig.Instance.BrowserRevision = localRevision.Revision;

                Console.WriteLine(">> Local revision is in actual version");
                return;
            }

            var localRevisions = browserFetcher.LocalRevisions();

            if (localRevisions.Any())
            {
                Console.WriteLine(">> Local revision is not in actual version - need to remove old revisions and download new one");

                foreach (string revision in browserFetcher.LocalRevisions())
                {
                    //browserFetcher.Remove(revision);
                }
            }
            else
            {
                Console.WriteLine(">> Installed revisions of Chrome not found - need to download new one");
            }

            Console.WriteLine(">> Start downloading Chrome...");

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

            Console.WriteLine("\n>> Chrome downloading complete!");
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
                Platform.Win32 => "Win",
                Platform.Win64 => "Win_x64",
                Platform.Linux => "Linux",
                Platform.MacOS => "Mac",
                _ => ""
            };

            if (string.IsNullOrEmpty(platformName))
            {
                return BrowserFetcher.DefaultChromiumRevision;
            }

            HttpClient client = new();

            var revDataResp = await client.GetAsync(new Uri($"https://www.googleapis.com/storage/v1/b/chromium-browser-snapshots/o/{platformName}%2FLAST_CHANGE"));

            JObject revData = JObject.Parse(await revDataResp.Content.ReadAsStringAsync());

            string actualRevisionLink = (string)revData["mediaLink"] ?? "";

            var actualRevResp = await client.GetAsync(new Uri(actualRevisionLink));

            return await actualRevResp.Content.ReadAsStringAsync();
        }

        public static Task<Browser> GetBrowser()
        {
            var browserFetcher = new BrowserFetcher();
            var installedRevision = browserFetcher.RevisionInfo(AppConfig.Instance.BrowserRevision);

            return Puppeteer.LaunchAsync(new LaunchOptions { Headless = true, ExecutablePath = installedRevision.ExecutablePath });
        }

        public static async Task LaunchBrowser()
        {
            AppConfig.Instance.Browser = await GetBrowser();
        }

        public static async Task CloseBrowser()
        {
            if (AppConfig.Instance.Browser != null)
            {
                await AppConfig.Instance.Browser.CloseAsync();
            }
        }
    }
}
