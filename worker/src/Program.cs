using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

var host = Environment.GetEnvironmentVariable("RABBITMQ__HOST") ?? "localhost";
var user = Environment.GetEnvironmentVariable("RABBITMQ__USER") ?? "guest";
var pass = Environment.GetEnvironmentVariable("RABBITMQ__PASS") ?? "guest";
var prefetch = int.TryParse(Environment.GetEnvironmentVariable("WORKER__PREFETCH"), out var p) ? p : 10;
var maxRetries = int.TryParse(Environment.GetEnvironmentVariable("WORKER__RETRYCOUNT"), out var r) ? r : 3;

const string exchange = "orders.exchange";
const string routingKey = "orders.created";
const string queue = "orders.queue";
const string dlxExchange = "orders.dlx";
const string dlq = "orders.dlq";

var processed = new ConcurrentDictionary<string, bool>();

while (true)
{
    try
    {
        var factory = new ConnectionFactory { HostName = host, UserName = user, Password = pass };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(exchange, "topic", durable: true);
        channel.ExchangeDeclare(dlxExchange, "fanout", durable: true);

        var qArgs = new Dictionary<string, object> { ["x-dead-letter-exchange"] = dlxExchange };
        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false, arguments: qArgs);
        channel.QueueBind(queue, exchange, routingKey);

        channel.QueueDeclare(dlq, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(dlq, dlxExchange, "");

        channel.BasicQos(0, (ushort)prefetch, false);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            var messageId = ea.BasicProperties?.MessageId ?? "";

            try
            {
                var doc = JsonDocument.Parse(json).RootElement;
                var orderId = doc.GetProperty("orderId").GetGuid().ToString();
                var amount  = doc.GetProperty("amount").GetDecimal();

                var key = string.IsNullOrWhiteSpace(messageId) ? orderId : messageId;
                if (!processed.TryAdd(key, true))
                {
                    channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }

                await Task.Delay(50); // simula trabajo
                channel.BasicAck(ea.DeliveryTag, false);
            }
            catch
            {
                int retries = 0;
                if (ea.BasicProperties?.Headers != null &&
                    ea.BasicProperties.Headers.TryGetValue("x-retry", out var val) &&
                    int.TryParse(val?.ToString(), out var parsed))
                {
                    retries = parsed;
                }

                if (retries + 1 >= maxRetries)
                {
                    channel.BasicReject(ea.DeliveryTag, requeue: false); // a DLQ
                }
                else
                {
                    var props = channel.CreateBasicProperties();
                    props.Headers = props.Headers ?? new Dictionary<string, object>();
                    props.Headers["x-retry"] = retries + 1;
                    props.DeliveryMode = 2;
                    props.ContentType  = "application/json";
                    channel.BasicPublish(exchange, routingKey, props, ea.Body);
                    channel.BasicAck(ea.DeliveryTag, false);
                }
            }
        };

        channel.BasicConsume(queue, autoAck: false, consumer);
        await Task.Delay(Timeout.Infinite);
    }
    catch
    {
        await Task.Delay(TimeSpan.FromSeconds(5)); // reconexión
    }
}