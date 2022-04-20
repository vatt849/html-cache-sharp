// See https://aka.ms/new-console-template for more information
using CommandLine;
using HtmlCache;
using HtmlCache.Config;
using HtmlCache.Process;
using Newtonsoft.Json;

Console.CancelKeyPress += async (sender, e) =>
{
    Console.WriteLine();
    Console.WriteLine("Task interrupted. Exit.");

    await BrowserLoader.CloseBrowserAsync();
};

return await Parser.Default.ParseArguments<CLOptions>(args)
    .MapResult(async (CLOptions opts) =>
    {
        try
        {
            var timeStart = DateTime.Now;

            Console.WriteLine($"BS HtmlCache started at {DateTime.Now:yyy-MM-dd HH:mm:ss}");

            Console.WriteLine();

            string configPath = opts.ConfigPath ?? "";

            Console.WriteLine($"Verbose output: {opts.Verbose}");
            Console.WriteLine($"Group urls before caching: {opts.GroupUrls}");
            Console.WriteLine($"Multithread mode: {opts.Multithread}");

            Console.WriteLine();

            if (!Path.IsPathRooted(configPath))
            {
                configPath = Path.GetFullPath(configPath);
            }

            FileAttributes attr = File.GetAttributes(configPath);

            if (attr.HasFlag(FileAttributes.Directory))
            {
                configPath = Path.Combine(configPath, "html-cache-config.yaml");
            }

            Console.WriteLine($"Init caching by config path: {configPath}");

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"ERROR! Unable to locate config by path: {configPath}. Exit.");
                return -1;
            }

            Console.WriteLine();

            var configTimeStart = DateTime.Now;
            Console.WriteLine("Reading config file...");

            AppConfig.Load(configPath);
            AppConfig.Instance.Verbose = opts.Verbose;
            AppConfig.Instance.Multithread = opts.Multithread;

            Console.WriteLine($"Config parsed successfully ({DateTime.Now - configTimeStart})");

            Console.WriteLine();

            var revisionTimeStart = DateTime.Now;
            Console.WriteLine("Checking browser revision...");

            if (!string.IsNullOrEmpty(opts.ChromeRevision))
            {
                AppConfig.Instance.BrowserRevision = opts.ChromeRevision;
            }

            await BrowserLoader.CheckRevisionAsync();

            Console.WriteLine($"Checking browser revision done ({DateTime.Now - revisionTimeStart})");

            if (AppConfig.Instance.Verbose)
            {
                Console.WriteLine();
                Console.WriteLine($"App config:\n{JsonConvert.SerializeObject(AppConfig.Instance, Formatting.Indented)}");
            }

            Console.WriteLine();

            var sitemapTimeStart = DateTime.Now;
            Console.WriteLine("Loading sitemap...");

            var urlset = Sitemap.LoadUrls();

            Console.WriteLine($"Sitemap loaded successfully ({DateTime.Now - sitemapTimeStart}). Urls count: {urlset.Count}");

            Console.WriteLine();

            var browserTimeStart = DateTime.Now;
            Console.WriteLine("Start browser process...");

            await BrowserLoader.LaunchBrowserAsync();

            Console.WriteLine($"Browser launched successfully ({DateTime.Now - browserTimeStart})");

            Console.WriteLine();

            var renderTimeStart = DateTime.Now;
            Console.WriteLine("Start rendering...");
            Renderer render = new(urlset);

            if (opts.GroupUrls)
            {
                await render.CollectGroupedAsync();
            }
            else
            {
                await render.CollectAsync();
            }

            Console.WriteLine($"Render successfully finished ({DateTime.Now - renderTimeStart}). Urls passed: {render.Passed} of {render.Total}");

            Console.WriteLine($"All tasks completed in {DateTime.Now - timeStart}");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error! {e.Message}\n{e.StackTrace}");
            return -3; // Unhandled error
        }
    },
    errs => Task.FromResult(-1)); // Invalid arguments