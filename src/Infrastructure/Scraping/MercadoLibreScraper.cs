using System.Globalization;
using System.Text.Json;
using System.Linq;
using EZPrice.Application.Common.Interfaces;
using EZPrice.Application.Common.Options;
using EZPrice.Application.Common.Queues;
using EZPrice.Application.Search.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace EZPrice.Infrastructure.Scraping;

public class MercadoLibreScraper : ISourceScraper
{
    private readonly ILogger<MercadoLibreScraper> _logger;
    private readonly PlaywrightScrapingOptions _options;
    private readonly TimeProvider _timeProvider;

    public MercadoLibreScraper(
        ILogger<MercadoLibreScraper> logger,
        IOptions<PlaywrightScrapingOptions> options,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _options = options.Value ?? new PlaywrightScrapingOptions();
        _timeProvider = timeProvider;
    }

    public string Source => SearchSources.MercadoLibre;

    public async Task<IReadOnlyList<SearchResultItem>> ScrapeAsync(SearchJob job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.Query))
        {
            return Array.Empty<SearchResultItem>();
        }

        if (!_options.Enabled)
        {
            _logger.LogInformation("Playwright scraping disabled via configuration.");
            return Array.Empty<SearchResultItem>();
        }

        var targetUrl = BuildSearchUrl(job.Query, job.Page);
        var pageTimeout = PlaywrightScraperHelper.NormalizeTimeout(_options.TimeoutMs, 120_000);
        var waitTimeout = PlaywrightScraperHelper.NormalizeTimeout(_options.WaitForSelectorMs, 30_000);
        var scrollCount = _options.ScrollCount <= 0 ? 10 : _options.ScrollCount;
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

        await page.WaitForSelectorAsync(
            "li.ui-search-layout__item",
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

            var priceAmount = ParseMoney(item.Price);
            if (!priceAmount.HasValue) continue;

            items.Add(new SearchResultItem(
                item.Title.Trim(),
                priceAmount.Value,
                NormalizeCurrency(item.PriceCurrency),
                item.Link.Trim(),
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

    private async Task<List<PlaywrightRawItem>> ExtractFastAsync(IPage page, CancellationToken cancellationToken)
    {
        try
        {
            var rawItemsJson = await page.EvaluateAsync<string?>("""
            () => {
                const cards = Array.from(document.querySelectorAll('li.ui-search-layout__item'));
                const items = cards.map(card => {
                    const text = (sel) => {
                        const el = card.querySelector(sel);
                        return el ? el.textContent.trim() : null;
                    };
                    const attr = (sel, name) => {
                        const el = card.querySelector(sel);
                        return el ? el.getAttribute(name) : null;
                    };
                    const title = text('.poly-component__title');
                    const link = attr('a.poly-component__title', 'href');
                    const price = text('.andes-money-amount__fraction');
                    const priceCurrency = text('.andes-money-amount__currency, .andes-money-amount__currency-symbol');
                    const image = attr('img', 'src') || attr('img', 'data-src');
                    return { title, link, price, priceCurrency, image };
                });
                return JSON.stringify(items);
            }
        """);

            if (string.IsNullOrWhiteSpace(rawItemsJson))
            {
                return new List<PlaywrightRawItem>();
            }

            using var doc = JsonDocument.Parse(rawItemsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new List<PlaywrightRawItem>();
            }

            var items = new List<PlaywrightRawItem>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                items.Add(new PlaywrightRawItem(
                    GetJsonString(item, "title"),
                    GetJsonString(item, "link"),
                    GetJsonString(item, "price"),
                    GetJsonString(item, "priceCurrency"),
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

    private static async Task<List<PlaywrightRawItem>> ExtractViaHandlesAsync(IPage page, CancellationToken cancellationToken)
    {
        var cards = await page.QuerySelectorAllAsync("li.ui-search-layout__item");
        var items = new List<PlaywrightRawItem>();
        foreach (var card in cards)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var title = await GetInnerTextAsync(card, ".poly-component__title");
            var link = await GetAttributeAsync(card, "a.poly-component__title", "href");
            var price = await GetInnerTextAsync(card, ".andes-money-amount__fraction");
            var priceCurrency = await GetInnerTextAsync(card, ".andes-money-amount__currency, .andes-money-amount__currency-symbol");
            var image = await GetAttributeAsync(card, "img", "src")
                        ?? await GetAttributeAsync(card, "img", "data-src");

            items.Add(new PlaywrightRawItem(title, link, price, priceCurrency, image));
        }

        return items;
    }

    private static string BuildSearchUrl(string query, int page)
    {
        var slug = string.Join('-', query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (page <= 1)
        {
            return $"https://listado.mercadolibre.com.ar/{Uri.EscapeDataString(slug)}";
        }

        return $"https://listado.mercadolibre.com.ar/{Uri.EscapeDataString(slug)}_Desde_{((page - 1) * 50) + 1}";
    }

    private static decimal? ParseMoney(string? rawAmount)
    {
        if (string.IsNullOrWhiteSpace(rawAmount))
        {
            return null;
        }

        var digits = new string(rawAmount.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
        {
            return null;
        }

        if (!decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        return amount;
    }

    private static string NormalizeCurrency(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "ARS";
        }

        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed.Contains("US") || trimmed.Contains("U$S") || trimmed.Contains("USD"))
        {
            return "USD";
        }

        if (trimmed.Contains("$") || trimmed.Contains("ARS"))
        {
            return "ARS";
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

    private sealed record PlaywrightRawItem(
        string? Title,
        string? Link,
        string? Price,
        string? PriceCurrency,
        string? Image
    );
}
