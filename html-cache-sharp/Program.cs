// See https://aka.ms/new-console-template for more information
using CommandLine;
using HtmlCache;
using HtmlCache.Config;
using HtmlCache.Process;
using log4net;
using log4net.Config;
using Newtonsoft.Json;
using System.Text;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

XmlConfigurator.Configure(new FileInfo("log4net.config"));
ILog log = LogManager.GetLogger("Main");

Console.CancelKeyPress += async (sender, e) =>
{
    log.Warn("Task interrupted. Exit.");

    await BrowserLoader.CloseBrowserAsync();
};

return await Parser.Default.ParseArguments<CLOptions>(args)
    .MapResult(async (CLOptions opts) =>
    {
        try
        {
            var timeStart = DateTime.Now;

            log.Info($"BS HtmlCache started at {DateTime.Now:yyy-MM-dd HH:mm:ss}");

            string configPath = opts.ConfigPath ?? "";

            log.Info($"Verbose output: {opts.Verbose}");
            log.Info($"Group urls before caching: {opts.GroupUrls}");
            log.Info($"Multithread mode: {opts.Multithread}");

            if (!Path.IsPathRooted(configPath))
            {
                configPath = Path.GetFullPath(configPath);
            }

            FileAttributes attr = File.GetAttributes(configPath);

            if (attr.HasFlag(FileAttributes.Directory))
            {
                configPath = Path.Combine(configPath, "html-cache-config.yaml");
            }

            log.Info($"Init caching by config path: {configPath}");

            if (!File.Exists(configPath))
            {
                log.Error($"ERROR! Unable to locate config by path: {configPath}. Exit.");
                return -1;
            }

            var configTimeStart = DateTime.Now;
            log.Info("Reading config file");

            AppConfig.Load(configPath);
            AppConfig.Instance.Verbose = opts.Verbose;
            AppConfig.Instance.Multithread = opts.Multithread;
            AppConfig.Instance.MaxThreads = opts.MaxThreads;

            log.Info($"Config parsed successfully ({DateTime.Now - configTimeStart})");

            Console.WriteLine();

            var revisionTimeStart = DateTime.Now;
            log.Info("Checking browser revision");

            if (!string.IsNullOrEmpty(opts.ChromeRevision))
            {
                AppConfig.Instance.BrowserRevision = opts.ChromeRevision;
            }

            await BrowserLoader.CheckRevisionAsync();

            log.Info($"Checking browser revision done ({DateTime.Now - revisionTimeStart})");

            if (AppConfig.Instance.Verbose)
            {
                log.Debug($"App config:\n{JsonConvert.SerializeObject(AppConfig.Instance, Formatting.Indented)}");
            }

            var sitemapTimeStart = DateTime.Now;
            log.Info("Loading sitemap");

            var urlset = Sitemap.LoadUrls();

            log.Info($"Sitemap loaded successfully ({DateTime.Now - sitemapTimeStart}). Urls count: {urlset.Count}");

            var browserTimeStart = DateTime.Now;
            log.Info("Start browser");

            await BrowserLoader.LaunchBrowserAsync();

            log.Info($"Browser launched successfully ({DateTime.Now - browserTimeStart})");

            var renderTimeStart = DateTime.Now;
            log.Info("Start rendering");
            Renderer render = new(urlset);

            if (opts.GroupUrls)
            {
                await render.CollectGroupedAsync();
            }
            else
            {
                await render.CollectAsync();
            }

            log.Info($"Render successfully finished ({DateTime.Now - renderTimeStart}). Urls passed: {render.Passed} of {render.Total}");

            log.Info($"All tasks completed in {DateTime.Now - timeStart}");
            return 0;
        }
        catch (Exception e)
        {
            log.Error($"Error! {e.Message}\n{e.StackTrace}");
            return -3; // Unhandled error
        }
    },
    errs => Task.FromResult(-1)); // Invalid arguments