using System.Globalization;

var random = new Random();
var serviceNames = new[]
{
    "OrderService", "PaymentService", "InventoryService", "BillingService", "NotificationService", "ShippingService"
};
var requestPaths = new[]
{
    "/api/orders/checkout", "/api/payments/capture", "/api/inventory/reserve", "/api/invoices/create", "/api/notifications/send"
};

var events = new (string Level, Func<Random, string> Build)[]
{
    ("ERROR", r => BuildUnhandledException(r)),
    ("ERROR", r => BuildHttpTimeoutException(r)),
    ("ERROR", r => BuildDatabaseDeadlock(r)),
    ("ERROR", r => BuildKafkaProcessingError(r)),
    ("WARN", r => BuildSlowDependencyWarning(r)),
    ("WARN", r => BuildCircuitBreakerOpen(r)),
    ("WARN", r => BuildRetryExhausted(r)),
    ("INFO", r => BuildSuccessfulRequest(r)),
};

while (true)
{
    var sample = events[random.Next(events.Length)];
    var traceId = Guid.NewGuid().ToString("N")[..16];
    var spanId = Guid.NewGuid().ToString("N")[..16];
    var service = serviceNames[random.Next(serviceNames.Length)];
    var path = requestPaths[random.Next(requestPaths.Length)];
    var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    var payload = sample.Build(random);

    Console.WriteLine(
        $"{timestamp} {sample.Level} [{service}] trace_id={traceId} span_id={spanId} path={path} " +
        $"request_id=req-{traceId[..8]} event=\"{payload}\"");
    await Task.Delay(TimeSpan.FromSeconds(2));
}

static string BuildUnhandledException(Random random)
{
    var line = random.Next(30, 240);
    var nestedLine = random.Next(15, 190);
    return "Unhandled exception while processing checkout command\\n" +
           "System.NullReferenceException: Object reference not set to an instance of an object.\\n" +
           $"   at LogAnalyzer.Api.Services.OrderOrchestrator.ExecuteCheckoutAsync(CheckoutCommand cmd) in /src/LogAnalyzer.Api/Services/OrderOrchestrator.cs:line {line}\\n" +
           $"   at LogAnalyzer.Api.Controllers.OrderController.PostCheckout(CheckoutRequest request) in /src/LogAnalyzer.Api/Controllers/OrderController.cs:line {nestedLine}\\n" +
           "   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskOfIActionResultExecutor.Execute(...)";
}

static string BuildHttpTimeoutException(Random random)
{
    var line = random.Next(40, 180);
    return "Downstream dependency timeout\\n" +
           "System.TimeoutException: The HTTP request to BillingApi timed out after 10s.\\n" +
           $"   at LogAnalyzer.Infrastructure.Clients.BillingClient.CapturePaymentAsync(PaymentRequest request) in /src/LogAnalyzer.Infrastructure/Clients/BillingClient.cs:line {line}\\n" +
           "   at System.Net.Http.HttpClient.<SendAsync>g__Core|83_0(...)\\n" +
           "Inner exception: System.Net.Sockets.SocketException (110): Connection timed out";
}

static string BuildDatabaseDeadlock(Random random)
{
    var line = random.Next(20, 160);
    return "Transaction aborted during account update\\n" +
           "Npgsql.PostgresException (0x80004005): 40P01: deadlock detected\\n" +
           $"   at LogAnalyzer.Infrastructure.Repositories.AccountRepository.UpdateBalanceAsync(Guid accountId, Decimal delta) in /src/LogAnalyzer.Infrastructure/Repositories/AccountRepository.cs:line {line}\\n" +
           "   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(...)";
}

static string BuildKafkaProcessingError(Random random)
{
    var line = random.Next(25, 220);
    return "Kafka consumer failed to process message\\n" +
           "Confluent.Kafka.ConsumeException: Broker: Unknown topic or partition\\n" +
           $"   at LogAnalyzer.Processor.Messaging.OrderEventsConsumer.HandleAsync(OrderEvent message) in /src/LogAnalyzer.Processor/Messaging/OrderEventsConsumer.cs:line {line}\\n" +
           "   at LogAnalyzer.Processor.Messaging.BackgroundConsumer.ExecuteAsync(CancellationToken stoppingToken)";
}

static string BuildSlowDependencyWarning(Random random)
{
    var elapsed = random.Next(1500, 6500);
    return $"Dependency latency high: InventoryApi took {elapsed} ms (p95 threshold 1200 ms). " +
           "Potential thread pool pressure under peak load.";
}

static string BuildCircuitBreakerOpen(Random random)
{
    var openSeconds = random.Next(10, 90);
    return $"Circuit breaker OPEN for NotificationProvider. Failures=8, OpenFor={openSeconds}s, " +
           "LastError=HttpRequestException: 503 Service Unavailable.";
}

static string BuildRetryExhausted(Random random)
{
    var line = random.Next(35, 140);
    return "Retry policy exhausted after 3 attempts for webhook dispatch\\n" +
           $"   at LogAnalyzer.Api.Services.WebhookDispatcher.DeliverAsync(WebhookPayload payload) in /src/LogAnalyzer.Api/Services/WebhookDispatcher.cs:line {line}\\n" +
           "LastStatusCode=408, Endpoint=https://partner.example.com/webhook/orders";
}

static string BuildSuccessfulRequest(Random random)
{
    var elapsed = random.Next(45, 420);
    var rows = random.Next(1, 80);
    return $"Request completed successfully in {elapsed} ms. Response=200, DBRowsAffected={rows}, CacheHit={(elapsed < 120).ToString().ToLowerInvariant()}";
}
