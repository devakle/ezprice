using EZPrice.Application.Common.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EZPrice.Worker;
using EZPrice.Application.Common.Interfaces;
using EZPrice.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection(SearchOptions.SectionName));
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection(RedisOptions.SectionName));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<ElasticsearchOptions>(builder.Configuration.GetSection(ElasticsearchOptions.SectionName));
builder.Services.Configure<PlaywrightScrapingOptions>(builder.Configuration.GetSection(PlaywrightScrapingOptions.SectionName));

builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.Services.AddScoped<IUser, WorkerUser>();

builder.Services.AddHostedService<SearchWorker>();

var host = builder.Build();

await host.RunAsync();
