using System.Text;
using System.Text.Json;
using System.Linq;
using EZPrice.Application.Common.Interfaces;
using EZPrice.Application.Common.Options;
using EZPrice.Application.Common.Queues;
using EZPrice.Application.Search.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EZPrice.Worker;

public class SearchWorker : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SearchOptions _options;
    private readonly ILogger<SearchWorker> _logger;
    private readonly List<IModel> _channels = new();

    public SearchWorker(
        IConnection connection,
        IServiceScopeFactory scopeFactory,
        IOptions<SearchOptions> options,
        ILogger<SearchWorker> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var source in _options.Sources)
        {
            var channel = _connection.CreateModel();
            _channels.Add(channel);

            var queueName = SearchQueueNames.ForSource(source);
            channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            channel.BasicQos(0, 1, false);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += async (_, args) =>
            {
                await ProcessMessageAsync(channel, args, stoppingToken);
            };

            channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
        }

        stoppingToken.Register(() =>
        {
            foreach (var channel in _channels)
            {
                channel.Close();
                channel.Dispose();
            }
        });

        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessageAsync(IModel channel, BasicDeliverEventArgs args, CancellationToken stoppingToken)
    {
        SearchJob? job = null;
        try
        {
            var body = Encoding.UTF8.GetString(args.Body.ToArray());
            job = JsonSerializer.Deserialize<SearchJob>(body, SerializerOptions);

            if (job is null)
            {
                channel.BasicAck(args.DeliveryTag, multiple: false);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var scrapers = scope.ServiceProvider.GetServices<ISourceScraper>();
            var scraper = scrapers.FirstOrDefault(s => s.Source == job.Source);

            if (scraper is null)
            {
                _logger.LogWarning("No scraper registered for source {Source}", job.Source);
                channel.BasicAck(args.DeliveryTag, multiple: false);
                return;
            }

            var items = await scraper.ScrapeAsync(job, stoppingToken);
            var store = scope.ServiceProvider.GetRequiredService<IScrapeResultStore>();
            await store.PersistAsync(job, items, stoppingToken);

            channel.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing search job for source {Source}", job?.Source ?? "unknown");
            channel.BasicNack(args.DeliveryTag, multiple: false, requeue: true);
        }
    }
}
