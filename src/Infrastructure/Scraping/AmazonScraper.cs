using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using EZPrice.Application.Common.Interfaces;
using EZPrice.Application.Common.Options;
using EZPrice.Application.Common.Queues;
using EZPrice.Application.Search.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace EZPrice.Infrastructure.Scraping;

public class AmazonScraper : ISourceScraper
{
    private static readonly Regex PriceRegex = new(@"(\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{2})?)", RegexOptions.Compiled);

    private readonly ILogger<AmazonScraper> _logger;
    private readonly PlaywrightScrapingOptions _options;
    private readonly TimeProvider _timeProvider;

    public AmazonScraper(
        ILogger<AmazonScraper> logger,
        IOptions<PlaywrightScrapingOptions> options,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _options = options.Value ?? new PlaywrightScrapingOptions();
        _timeProvider = timeProvider;
    }

    public string Source => SearchSources.Amazon;

    public async Task<IReadOnlyList<SearchResultItem>> ScrapeAsync(SearchJob job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.Query))
        {
            return Array.Empty<SearchResultItem>();
        }

        if (!_options.Enabled)
        {
            _logger.LogInformation("Amazon Playwright scraping disabled via configuration.");
            return Array.Empty<SearchResultItem>();
        }

        var targetUrl = BuildSearchUrl(job.Query, job.Page);
        var pageTimeout = PlaywrightScraperHelper.NormalizeTimeout(_options.TimeoutMs, 120_000);
        var waitTimeout = PlaywrightScraperHelper.NormalizeTimeout(_options.WaitForSelectorMs, 30_000);
        var scrollCount = _options.ScrollCount <= 0 ? 2 : _options.ScrollCount;
        var scrollWait = _options.ScrollWaitMs <= 0 ? 200 : _options.ScrollWaitMs;

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await CreateBrowserAsync(playwright, cancellationToken);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = string.IsNullOrWhiteSpace(_options.UserAgent) ? null : _options.UserAgent,
            AcceptDownloads = false
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(pageTimeout);

        await page.GotoAsync(targetUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = pageTimeout
        });

        await TryAcceptCookiesAsync(page);

        await page.WaitForSelectorAsync(
            "div[data-component-type='s-search-result']",
            new PageWaitForSelectorOptions { Timeout = waitTimeout });

        for (var i = 0; i < scrollCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
            await page.WaitForTimeoutAsync(scrollWait);
        }

        var rawItems = await ExtractFastAsync(page, cancellationToken);
        var now = _timeProvider.GetUtcNow();

        var items = new List<SearchResultItem>();
        foreach (var item in rawItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is null) continue;
            if (string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Link)) continue;

            var link = NormalizeAmazonLink(item.Link);
            var priceAmount = ParseMoney(item.PriceWhole);
            if (!priceAmount.HasValue) continue;

            items.Add(new SearchResultItem(
                item.Title.Trim(),
                priceAmount.Value,
                NormalizeCurrency(item.PriceSymbol),
                link,
                Source,
                now,
                item.Image?.Trim()));
        }

        return items;
    }

    private async Task<IBrowser> CreateBrowserAsync(IPlaywright playwright, CancellationToken cancellationToken)
    {
        if (_options.UseCdpIfAvailable)
        {
            var cdpEndpoint = Environment.GetEnvironmentVariable("PLAYWRIGHT_CDP_URL");
            if (!string.IsNullOrWhiteSpace(cdpEndpoint))
            {
                return await playwright.Chromium.ConnectOverCDPAsync(cdpEndpoint);
            }
        }

        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = _options.Headless,
            SlowMo = _options.SlowMoMs,
            ExecutablePath = string.IsNullOrWhiteSpace(_options.BrowserPath) ? null : _options.BrowserPath
        };

        return await playwright.Chromium.LaunchAsync(launchOptions);
    }

    private async Task<List<AmazonRawItem>> ExtractFastAsync(IPage page, CancellationToken cancellationToken)
    {
        try
        {
            var rawItemsJson = await page.EvaluateAsync<string?>("""
            () => {
                const cards = Array.from(document.querySelectorAll("div[data-component-type='s-search-result']"));
                const text = (root, sel) => {
                    const el = root.querySelector(sel);
                    if (!el) return null;
                    const value = el.textContent ? el.textContent.trim() : null;
                    return value || null;
                };
                const attr = (root, sel, name) => {
                    const el = root.querySelector(sel);
                    if (!el) return null;
                    const value = el.getAttribute(name);
                    return value ? value.trim() : null;
                };
                const items = cards.map(card => {
                    const title = text(card, "h2 span") || text(card, ".s-title-instructions-style h2 span");
                    const link = attr(card, "h2 a", "href") || attr(card, "a.a-link-normal", "href");
                    const priceWhole = text(card, "span.a-price > span.a-offscreen") || text(card, "span.a-price-whole");
                    const priceSymbol = text(card, "span.a-price-symbol");
                    const originalPrice = text(card, "span.a-text-price > span.a-offscreen");
                    const shipping = text(card, "[data-cy='delivery-recipe'] span")
                        || text(card, "span.a-color-base.a-text-bold")
                        || text(card, "span.a-color-secondary");
                    const image = attr(card, "img.s-image", "src") || attr(card, "img.s-image", "data-src");
                    return { title, link, priceWhole, priceSymbol, originalPrice, shipping, image };
                }).filter(item => item && item.title && item.link);
                return JSON.stringify(items);
            }
        """);

            if (string.IsNullOrWhiteSpace(rawItemsJson))
            {
                return new List<AmazonRawItem>();
            }

            using var doc = JsonDocument.Parse(rawItemsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new List<AmazonRawItem>();
            }

            var items = new List<AmazonRawItem>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var title = GetJsonString(item, "title");
                var link = GetJsonString(item, "link");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                items.Add(new AmazonRawItem(
                    title,
                    link,
                    GetJsonString(item, "priceWhole"),
                    GetJsonString(item, "priceSymbol"),
                    GetJsonString(item, "originalPrice"),
                    GetJsonString(item, "shipping"),
                    GetJsonString(item, "image")
                ));
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fast extraction failed. Falling back to per-card scraping.");
            return await ExtractViaHandlesAsync(page, cancellationToken);
        }
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static async Task<List<AmazonRawItem>> ExtractViaHandlesAsync(IPage page, CancellationToken cancellationToken)
    {
        var cards = await page.QuerySelectorAllAsync(".sg-col-4-of-24");
        var items = new List<AmazonRawItem>();

        foreach (var card in cards)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var title = await GetInnerTextAsync(card, "h2 span")
                        ?? await GetInnerTextAsync(card, ".s-title-instructions-style h2 span");
            var link = await GetAttributeAsync(card, "h2 a", "href")
                       ?? await GetAttributeAsync(card, "a.a-link-normal", "href");
            var priceWhole = await GetInnerTextAsync(card, "span.a-price > span.a-offscreen")
                             ?? await GetInnerTextAsync(card, "span.a-price-whole");
            var priceSymbol = await GetInnerTextAsync(card, "span.a-price-symbol");
            var originalPrice = await GetInnerTextAsync(card, "span.a-text-price > span.a-offscreen");
            var shipping = await GetInnerTextAsync(card, "[data-cy='delivery-recipe'] span")
                           ?? await GetInnerTextAsync(card, "span.a-color-base.a-text-bold")
                           ?? await GetInnerTextAsync(card, "span.a-color-secondary");
            var image = await GetAttributeAsync(card, "img.s-image", "src")
                        ?? await GetAttributeAsync(card, "img.s-image", "data-src");

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            items.Add(new AmazonRawItem(
                title,
                link,
                priceWhole,
                priceSymbol,
                originalPrice,
                shipping,
                image
            ));
        }

        return items;
    }

    private static string BuildSearchUrl(string query, int page)
    {
        var encoded = Uri.EscapeDataString(query);
        if (page <= 1)
        {
            return $"https://www.amazon.com/s?k={encoded}";
        }

        return $"https://www.amazon.com/s?k={encoded}&page={page}";
    }

    private static async Task TryAcceptCookiesAsync(IPage page)
    {
        try
        {
            await page.ClickAsync("#sp-cc-accept", new PageClickOptions { Timeout = 2_000 });
        }
        catch
        {
            // Ignore if cookie banner not present.
        }
    }

    private static string NormalizeAmazonLink(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;
        if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return raw;
        return $"https://www.amazon.com{raw}";
    }

    private static decimal? ParseMoney(string? rawAmount)
    {
        if (string.IsNullOrWhiteSpace(rawAmount))
        {
            return null;
        }

        var match = PriceRegex.Match(rawAmount);
        if (!match.Success) return null;

        var raw = match.Groups[1].Value;
        var normalized = raw.Replace(".", string.Empty).Replace(",", ".");
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        return amount;
    }

    private static string NormalizeCurrency(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "USD";
        }

        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed.Contains("US") || trimmed.Contains("USD"))
        {
            return "USD";
        }

        if (trimmed.Contains("$"))
        {
            return "USD";
        }

        return trimmed;
    }

    private static async Task<string?> GetInnerTextAsync(IElementHandle card, string selector)
    {
        var element = await card.QuerySelectorAsync(selector);
        if (element is null) return null;
        var text = await element.InnerTextAsync();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static async Task<string?> GetAttributeAsync(IElementHandle card, string selector, string attribute)
    {
        var element = await card.QuerySelectorAsync(selector);
        if (element is null) return null;
        var value = await element.GetAttributeAsync(attribute);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record AmazonRawItem(
        string? Title,
        string? Link,
        string? PriceWhole,
        string? PriceSymbol,
        string? OriginalPrice,
        string? Shipping,
        string? Image
    );
}
