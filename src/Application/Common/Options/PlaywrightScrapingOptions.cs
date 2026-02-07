namespace EZPrice.Application.Common.Options;

public class PlaywrightScrapingOptions
{
    public const string SectionName = "Scraping";

    public bool Enabled { get; set; } = true;
    public bool Headless { get; set; } = true;
    public int TimeoutMs { get; set; } = 120_000;
    public int WaitForSelectorMs { get; set; } = 30_000;
    public int ScrollWaitMs { get; set; } = 200;
    public int ScrollCount { get; set; } = 10;
    public float? SlowMoMs { get; set; }
    public string? UserAgent { get; set; } = "EZPrice/1.0";
    public string? BrowserPath { get; set; }
    public string? DownloadPath { get; set; }
    public bool UseCdpIfAvailable { get; set; } = true;
}
