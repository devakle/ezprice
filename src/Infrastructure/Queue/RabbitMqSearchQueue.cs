using System.Text;
using System.Text.Json;
using EZPrice.Application.Common.Interfaces;
using EZPrice.Application.Common.Queues;
using EZPrice.Application.Search.Models;
using RabbitMQ.Client;

namespace EZPrice.Infrastructure.Queue;

public class RabbitMqSearchQueue : ISearchQueue
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnection _connection;

    public RabbitMqSearchQueue(IConnection connection)
    {
        _connection = connection;
    }

    public Task EnqueueAsync(SearchJob job, CancellationToken cancellationToken)
    {
        using var channel = _connection.CreateModel();
        var queueName = SearchQueueNames.ForSource(job.Source);

        channel.QueueDeclare(queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(job, SerializerOptions));
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;

        channel.BasicPublish(exchange: string.Empty,
            routingKey: queueName,
            basicProperties: properties,
            body: body);

        return Task.CompletedTask;
    }
}
