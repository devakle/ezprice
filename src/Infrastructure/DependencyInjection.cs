using EZPrice.Application.Common.Interfaces;
using EZPrice.Application.Common.Options;
using EZPrice.Domain.Constants;
using EZPrice.Infrastructure.Cache;
using EZPrice.Infrastructure.Data;
using EZPrice.Infrastructure.Data.Interceptors;
using EZPrice.Infrastructure.Identity;
using EZPrice.Infrastructure.Queue;
using EZPrice.Infrastructure.Search;
using EZPrice.Infrastructure.Scraping;
using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("EZPriceDb");
        Guard.Against.Null(connectionString, message: "Connection string 'EZPriceDb' not found.");
        var databaseProvider = builder.Configuration.GetValue<string>("DatabaseProvider");

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            if (string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connectionString);
            }
            else
            {
                options.UseSqlServer(connectionString);
            }
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });


        builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();

        builder.Services
            .AddDefaultIdentity<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddTransient<IIdentityService, IdentityService>();

        builder.Services.AddAuthorization(options =>
            options.AddPolicy(Policies.CanPurge, policy => policy.RequireRole(Roles.Administrator)));

        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            config.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(config);
        });

        builder.Services.AddSingleton<ISearchCache, RedisSearchCache>();
        builder.Services.AddSingleton<ISearchJobDeduper, RedisSearchJobDeduper>();

        builder.Services.AddSingleton<IConnectionFactory>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
            return new ConnectionFactory
            {
                HostName = options.Host,
                Port = options.Port,
                UserName = options.User,
                Password = options.Password,
                VirtualHost = options.VirtualHost,
                DispatchConsumersAsync = true
            };
        });

        builder.Services.AddSingleton(sp => sp.GetRequiredService<IConnectionFactory>().CreateConnection());
        builder.Services.AddSingleton<ISearchQueue, RabbitMqSearchQueue>();

        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;
            return new ElasticsearchClient(new Uri(options.Uri));
        });
        builder.Services.AddSingleton<ISearchIndex, ElasticsearchIndex>();
        builder.Services.AddScoped<IScrapeResultStore, ScrapeResultStore>();

        builder.Services.AddScoped<ISourceScraper, MercadoLibreScraper>();
        builder.Services.AddScoped<ISourceScraper, AmazonScraper>();
    }
}
