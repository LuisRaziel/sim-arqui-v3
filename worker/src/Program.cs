using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Serilog;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;

// ===== Bootstrap de logs JSON compactos (listo para ELK/Datadog) =====
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateLogger();

Serilog.Log.Information("worker_starting");

// ===== Config por entorno =====
var host = Environment.GetEnvironmentVariable("RABBITMQ__HOST") ?? "localhost";
var user = Environment.GetEnvironmentVariable("RABBITMQ__USER") ?? "guest";
var pass = Environment.GetEnvironmentVariable("RABBITMQ__PASS") ?? "guest";
var prefetch = int.TryParse(Environment.GetEnvironmentVariable("WORKER__PREFETCH"), out var p) ? p : 10;
var maxRetries = int.TryParse(Environment.GetEnvironmentVariable("WORKER__RETRYCOUNT"), out var r) ? r : 3;

const string exchange    = "orders.exchange";
const string routingKey  = "orders.created";
const string queue       = "orders.queue";
const string dlxExchange = "orders.dlx";
const string dlq         = "orders.dlq";

// ===== Idempotencia con TTL (evita crecimiento infinito) =====
var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10_000 });
bool TryMarkProcessed(string key)
{
    if (cache.TryGetValue(key, out _)) return false;
    cache.Set(key, true, new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        Size = 1
    });
    return true;
}

// ===== Helper para leer x-retry de headers con tipos variados =====
static int GetRetryCount(IBasicProperties? props)
{
    try
    {
        if (props?.Headers is null) return 0;
        if (!props.Headers.TryGetValue("x-retry", out var raw) || raw is null) return 0;

        return raw switch
        {
            byte[] b                   => int.TryParse(Encoding.UTF8.GetString(b), out var i1) ? i1 : 0,
            ReadOnlyMemory<byte> mem   => int.TryParse(Encoding.UTF8.GetString(mem.ToArray()), out var i2) ? i2 : 0,
            sbyte sb                   => (int)sb,
            byte bb                    => (int)bb,
            short s                    => (int)s,
            ushort us                  => (int)us,
            int ii                     => ii,
            uint ui                    => (int)ui,
            long l                     => (int)l,
            ulong ul                   => (int)ul,
            string str                 => int.TryParse(str, out var i3) ? i3 : 0,
            _                          => 0
        };
    }
    catch { return 0; }
}

// ===== Heartbeat de vida (útil en Docker/K8s) =====
_ = Task.Run(async () =>
{
    while (true)
    {
        Serilog.Log.Information("worker_heartbeat");
        await Task.Delay(TimeSpan.FromSeconds(30));
    }
});

while (true)
{
    try
    {
        var factory = new ConnectionFactory
        {
            HostName = host,
            UserName = user,
            Password = pass,
            DispatchConsumersAsync = true,           // necesario para AsyncEventingBasicConsumer
            AutomaticRecoveryEnabled = true,         // reconexión automática
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedHeartbeat = TimeSpan.FromSeconds(30)
        };

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        // Topología (durable, sin autodelete)
        channel.ExchangeDeclare(exchange,    type: "topic",  durable: true, autoDelete: false);
        channel.ExchangeDeclare(dlxExchange, type: "fanout", durable: true, autoDelete: false);

        var qArgs = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"]    = dlxExchange,
            ["x-dead-letter-routing-key"] = ""
        };

        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false, arguments: qArgs);
        channel.QueueBind(queue, exchange, routingKey);

        channel.QueueDeclare(dlq, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(dlq, dlxExchange, routingKey: "");

        channel.BasicQos(0, (ushort)prefetch, false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            var started = Stopwatch.GetTimestamp();
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            var messageId = ea.BasicProperties?.MessageId ?? "";

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root    = doc.RootElement;
                var orderId = root.GetProperty("orderId").GetGuid().ToString();
                var amount  = root.GetProperty("amount").GetDecimal();

                Serilog.Log.Information("order_processing {OrderId} {Amount} {MessageId} {DeliveryTag}",
                    orderId, amount, messageId, ea.DeliveryTag);

                var key = string.IsNullOrWhiteSpace(messageId) ? orderId : messageId;
                if (!TryMarkProcessed(key))
                {
                    Serilog.Log.Information("duplicate_ignored {Key} {DeliveryTag}", key, ea.DeliveryTag);
                    channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }

                // Simula trabajo
                await Task.Delay(50);

                channel.BasicAck(ea.DeliveryTag, false);

                var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Serilog.Log.Information("order_processed {OrderId} {ElapsedMs}", orderId, elapsedMs);
            }
            catch (Exception ex)
            {
                var retries = GetRetryCount(ea.BasicProperties);

                if (retries + 1 >= maxRetries)
                {
                    Serilog.Log.Error(ex, "worker_error_to_dlq {DeliveryTag} {Retries}", ea.DeliveryTag, retries + 1);
                    channel.BasicReject(ea.DeliveryTag, requeue: false); // a DLQ
                }
                else
                {
                    var props = channel.CreateBasicProperties();
                    props.Headers ??= new Dictionary<string, object>();
                    props.Headers["x-retry"] = retries + 1;
                    props.DeliveryMode = 2;
                    props.ContentType  = "application/json";

                    channel.BasicPublish(exchange, routingKey, props, ea.Body);
                    channel.BasicAck(ea.DeliveryTag, false);

                    Serilog.Log.Error(ex, "worker_error_retrying {Retry} {DeliveryTag}", retries + 1, ea.DeliveryTag);
                }
            }
        };

        channel.BasicConsume(queue, autoAck: false, consumer);

        // Mantener hilo vivo
        await Task.Delay(Timeout.Infinite);
    }
    catch (Exception ex)
    {
        Serilog.Log.Error(ex, "worker_error_processing");
        await Task.Delay(TimeSpan.FromSeconds(5)); // backoff
    }
}