using Microsoft.Playwright;
using EZPrice.Application.Common.Options;

namespace EZPrice.Infrastructure.Scraping;

public static class PlaywrightScraperHelper
{
    public static async Task<(IPlaywright Playwright, IBrowser Browser, IBrowserContext Context, IPage Page)> CreatePageAsync(
        PlaywrightScrapingOptions options,
        CancellationToken cancellationToken)
    {
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        if (options.UseCdpIfAvailable)
        {
            var cdpEndpoint = Environment.GetEnvironmentVariable("PLAYWRIGHT_CDP_URL");
            if (!string.IsNullOrWhiteSpace(cdpEndpoint))
            {
                var cdpBrowser = await playwright.Chromium.ConnectOverCDPAsync(cdpEndpoint);
                var cdpContext = cdpBrowser.Contexts.FirstOrDefault() ?? await cdpBrowser.NewContextAsync();
                var cdpPage = await cdpContext.NewPageAsync();
                return (playwright, cdpBrowser, cdpContext, cdpPage);
            }
        }

        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = options.Headless,
            SlowMo = options.SlowMoMs,
            ExecutablePath = string.IsNullOrWhiteSpace(options.BrowserPath) ? null : options.BrowserPath
        };

        var browser = await playwright.Chromium.LaunchAsync(launchOptions);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = string.IsNullOrWhiteSpace(options.UserAgent) ? null : options.UserAgent,
            AcceptDownloads = false
        });

        var page = await context.NewPageAsync();
        return (playwright, browser, context, page);
    }

    public static int NormalizeTimeout(int value, int fallback) => value <= 0 ? fallback : value;
}
