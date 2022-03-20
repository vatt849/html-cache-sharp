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

                Console.Write($"\r>> Download progress: {e.ProgressPercentage}% [{bytesPerSecond / 1024} KB/s]");
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
            HttpClient client = new();
            var response = await client.GetAsync(new Uri("https://omahaproxy.appspot.com/json?channel=stable"));


            Dictionary<Platform, string> revisions = new();

            JArray osVersions = JArray.Parse(await response.Content.ReadAsStringAsync());

            foreach (var osObj in osVersions)
            {
                var osVersion = (JObject)osObj;

                string os = (string)osVersion["os"] ?? "";

                var actualVer = (JObject?)osVersion["versions"]?.First;

                if (actualVer is null) continue;

                string actualRevision = (string)actualVer["branch_base_position"] ?? BrowserFetcher.DefaultChromiumRevision;

                switch (os)
                {
                    case "win":
                        revisions[Platform.Win32] = actualRevision;
                        break;
                    case "win64":
                        revisions[Platform.Win64] = actualRevision;
                        break;
                    case "linux":
                        revisions[Platform.Linux] = actualRevision;
                        break;
                    case "mac":
                        revisions[Platform.MacOS] = actualRevision;
                        break;
                }
            }

            revisions[Platform.Unknown] = BrowserFetcher.DefaultChromiumRevision;

            return revisions;
        }

        public static async Task<string> GetActualRevisionAsync()
        {
            var revList = await GetActualRevisionsListAsync();

            var browserFetcher = new BrowserFetcher();

            return revList[browserFetcher.Platform] ?? BrowserFetcher.DefaultChromiumRevision;
        }

        public static Task<Browser> GetBrowser()
        {
            var browserFetcher = new BrowserFetcher();
            var installedRevision = browserFetcher.RevisionInfo(AppConfig.Instance.BrowserRevision);

            return Puppeteer.LaunchAsync(new LaunchOptions { Headless = true, ExecutablePath = installedRevision.ExecutablePath });
        }
    }
}
