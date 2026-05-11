internal static class Program
{
    private static readonly string[] Services =
    {
        "WebBff", "CheckoutSpaHost", "OrderService", "PaymentService", "InventoryService",
        "BillingService", "NotificationService", "ShippingService", "IdentityService",
        "ApiGateway", "SearchIndexer", "AnalyticsCollector",
    };

    private static readonly string[] Paths =
    {
        "/api/checkout/session", "/api/cart/merge", "/api/orders", "/api/payments/capture",
        "/api/inventory/reserve", "/api/invoices/create", "/api/notifications/send",
        "/api/auth/token", "/api/auth/introspect", "/internal/health", "/metrics",
        "/api/partners/webhook", "/graphql", "/cdn/asset-manifest",
    };

    private static readonly (int W, Scenario S)[] Weighted =
    {
        (18, new Scenario("INFO", "info", "backend", "routine_success", "none", BuildRoutineSuccess)),
        (5, new Scenario("WARN", "low", "frontend", "layout_warning", "monitor_only", BuildFrontendHydrationWarning)),
        (3, new Scenario("ERROR", "high", "frontend", "asset_failure", "no_immediate_action", BuildFrontendChunkLoadFailure)),
        (4, new Scenario("ERROR", "high", "backend", "unhandled_exception", "no_immediate_action", BuildUnhandledException)),
        (6, new Scenario("WARN", "low", "backend", "validation_noise", "backlog_candidate", BuildBackendValidationNoise)),
        (5, new Scenario("INFO", "info", "backend", "idempotent_replay", "monitor_only", BuildBackendIdempotentReplay)),
        (2, new Scenario("ERROR", "critical", "infrastructure", "compute_pressure", "no_immediate_action", BuildPodOomKilled)),
        (4, new Scenario("WARN", "medium", "infrastructure", "capacity_signal", "backlog_candidate", BuildDiskPressureWarning)),
        (3, new Scenario("WARN", "low", "infrastructure", "certificate_warning", "monitor_only", BuildTlsCertExpiring)),
        (7, new Scenario("WARN", "low", "authentication", "token_rejected", "monitor_only", BuildAuthInvalidToken)),
        (3, new Scenario("ERROR", "high", "authentication", "token_refresh_failure", "user_impacting_failure", BuildOAuthRefreshStorm)),
        (4, new Scenario("WARN", "medium", "authentication", "forbidden", "backlog_candidate", BuildPermissionDenied)),
        (6, new Scenario("ERROR", "medium", "external_service", "upstream_unavailable", "transient_recoverable", BuildPartner503)),
        (3, new Scenario("ERROR", "high", "external_service", "webhook_integrity", "no_immediate_action", BuildWebhookSignatureFailure)),
        (5, new Scenario("WARN", "low", "external_service", "rate_limit", "transient_recoverable", BuildExternalRateLimit)),
        (5, new Scenario("WARN", "info", "warning_noise", "deprecation", "backlog_candidate", BuildDeprecationNoise)),
        (4, new Scenario("INFO", "info", "warning_noise", "low_signal", "monitor_only", BuildNoisyRetryLogged)),
        (4, new Scenario("INFO", "low", "backend", "tech_debt_signal", "backlog_candidate", BuildBacklogLintDebt)),
        (4, new Scenario("ERROR", "medium", "backend", "transient_db", "transient_recoverable", BuildDatabaseDeadlock)),
        (4, new Scenario("ERROR", "medium", "infrastructure", "messaging", "transient_recoverable", BuildKafkaProcessingError)),
        (8, new Scenario("WARN", "medium", "backend", "latency_performance", "monitor_only", BuildLatencyP99Spike)),
        (2, new Scenario("WARN", "high", "backend", "latency_performance", "user_impacting_failure", BuildCacheStampede)),
        (2, new Scenario("ERROR", "critical", "backend", "payment_failure", "user_impacting_failure", BuildCheckoutPaymentCritical)),
        (5, new Scenario("ERROR", "medium", "external_service", "timeout", "transient_recoverable", BuildHttpTimeoutException)),
        (4, new Scenario("WARN", "medium", "external_service", "circuit_open", "monitor_only", BuildCircuitBreakerOpen)),
        (3, new Scenario("WARN", "high", "external_service", "retry_exhausted", "no_immediate_action", BuildRetryExhausted)),
    };

    private static async Task Main(string[] args)
    {
        if (args is { Length: > 0 } && args.Any(a => string.Equals(a, "--emit-fixed-multi-problem-batch", StringComparison.OrdinalIgnoreCase)))
        {
            EmitFixedMultiProblemBatch();
            return;
        }

        var random = new Random();
        var totalWeight = 0;
        foreach (var (w, _) in Weighted)
        {
            totalWeight += w;
        }

        while (true)
        {
            var pick = random.Next(totalWeight);
            var scenario = Weighted[0].S;
            foreach (var (w, s) in Weighted)
            {
                pick -= w;
                if (pick < 0)
                {
                    scenario = s;
                    break;
                }
            }

            var traceId = Guid.NewGuid().ToString("N")[..16];
            var spanId = Guid.NewGuid().ToString("N")[..16];
            var service = Services[random.Next(Services.Length)];
            var path = Paths[random.Next(Paths.Length)];
            var payload = scenario.Build(random);

            Console.WriteLine(
                $"{scenario.Level} [{service}] eval_severity={scenario.Severity} eval_scenario_domain={scenario.Domain} " +
                $"eval_scenario_kind={scenario.Kind} eval_incident_profile={scenario.Profile} trace_id={traceId} span_id={spanId} path={path} " +
                $"request_id=req-{traceId[..8]} event=\"{payload}\"");

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private static void EmitFixedMultiProblemBatch()
    {
        var random = new Random(42);

        var traceId1 = "aaaaaaaaaaaaaaaa";
        var spanId1 = "bbbbbbbbbbbbbbbb";
        var payloadDeadlock = BuildDatabaseDeadlock(random);
        Console.WriteLine(
            $"ERROR [OrderService] eval_severity=high eval_scenario_domain=backend " +
            $"trace_id={traceId1} span_id={spanId1} request_id=req-deadlock " +
            $"path=/api/orders event=\"{payloadDeadlock}\"");

        var traceId2 = "cccccccccccccccc";
        var spanId2 = "dddddddddddddddd";
        var payloadTimeout = BuildHttpTimeoutException(random);
        Console.WriteLine(
            $"ERROR [PaymentService] eval_severity=high eval_scenario_domain=external_service " +
            $"trace_id={traceId2} span_id={spanId2} request_id=req-timeout " +
            $"path=/api/payments/capture event=\"{payloadTimeout}\"");

        var traceId3 = "eeeeeeeeeeeeeeee";
        var spanId3 = "ffffffffffffffff";
        var payloadTls = BuildTlsCertExpiring(random);
        Console.WriteLine(
            $"WARN [ApiGateway] eval_severity=low eval_scenario_domain=infrastructure " +
            $"trace_id={traceId3} span_id={spanId3} request_id=req-tls " +
            $"path=/internal/health event=\"{payloadTls}\"");
    }

    private readonly record struct Scenario(
        string Level,
        string Severity,
        string Domain,
        string Kind,
        string Profile,
        Func<Random, string> Build);

    private static string BuildRoutineSuccess(Random random)
    {
        var elapsed = random.Next(32, 380);
        var rows = random.Next(0, 120);
        return $"request_completed_ms={elapsed} status=200 cache_hit={(elapsed < 95).ToString().ToLowerInvariant()} rows={rows}";
    }

    private static string BuildFrontendHydrationWarning(Random random)
    {
        var route = random.Next(0, 2) == 0 ? "/checkout" : "/account/settings";
        return $"SSR markup mismatch after navigation route={route} client_build={random.Next(4100, 4199)} server_build={random.Next(4100, 4199)}";
    }

    private static string BuildFrontendChunkLoadFailure(Random random)
    {
        var chunk = random.Next(0, 2) == 0 ? "checkout~vendor" : "account~profile";
        return $"dynamic_import_failed chunk={chunk} error=ChunkLoadError status=net::ERR_ABORTED retry_scheduled=true";
    }

    private static string BuildUnhandledException(Random random)
    {
        var line = random.Next(30, 240);
        var nestedLine = random.Next(15, 190);
        return "Unhandled exception while processing checkout command\\n" +
               "System.NullReferenceException: Object reference not set to an instance of an object.\\n" +
               $"   at OrderOrchestrator.ExecuteCheckoutAsync(CheckoutCommand cmd) in /src/Services/OrderOrchestrator.cs:line {line}\\n" +
               $"   at OrderController.PostCheckout(CheckoutRequest request) in /src/Controllers/OrderController.cs:line {nestedLine}\\n" +
               "   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.TaskOfIActionResultExecutor.Execute(...)";
    }

    private static string BuildBackendValidationNoise(Random random)
    {
        var field = random.Next(0, 3) switch { 0 => "shipping.zip", 1 => "payment.method", _ => "buyer.email" };
        return $"validation_failed field={field} code=FIELD_INVALID trace_category=noise suppress_alert=true";
    }

    private static string BuildBackendIdempotentReplay(Random random)
    {
        var key = Guid.NewGuid().ToString("N")[..12];
        return $"idempotent_hit dedupe_key={key} prior_status=201 replay_suppressed=true";
    }

    private static string BuildPodOomKilled(Random random)
    {
        var pod = $"order-service-{random.Next(1, 9)}-{random.Next(10, 99)}";
        return $"kube_event type=OOMKilled pod={pod} namespace=prod memory_limit_mi=512 restart_policy=Always";
    }

    private static string BuildDiskPressureWarning(Random random)
    {
        var pct = random.Next(82, 96);
        var mount = random.Next(0, 2) == 0 ? "/var/lib/containerd" : "/data/pg_wal";
        return $"host_disk_pressure mount={mount} used_percent={pct} watermark_high=85 ticket=INFRA-{random.Next(1000, 9999)}";
    }

    private static string BuildTlsCertExpiring(Random random)
    {
        var days = random.Next(5, 45);
        return $"tls_certificate_expiry_soon cn=api.example.com days_remaining={days} issuer=PublicCA rotation_ticket=backlog";
    }

    private static string BuildAuthInvalidToken(Random random)
    {
        return $"token_validation_failed reason=signature_invalid aud=checkout-api client_id=spa-{random.Next(100, 999)} outcome=401";
    }

    private static string BuildOAuthRefreshStorm(Random random)
    {
        var region = random.Next(0, 2) == 0 ? "eu-west-1" : "us-east-1";
        return $"refresh_token_endpoint_errors_spike region={region} error=invalid_grant burst_window_s=120 incidents_open=3";
    }

    private static string BuildPermissionDenied(Random random)
    {
        var scope = random.Next(0, 2) == 0 ? "orders:write" : "billing:read";
        return $"authorization_denied subject_role=support_ro scope={scope} resource=/api/admin/refunds decision=403";
    }

    private static string BuildPartner503(Random random)
    {
        var host = random.Next(0, 2) == 0 ? "tax-engine.partner.io" : "fraud-score.vendor.net";
        return $"upstream_http_failure host={host} status=503 attempts=1 retry_after_ms=0 circuit=half_open";
    }

    private static string BuildWebhookSignatureFailure(Random random)
    {
        return $"webhook_signature_mismatch provider=payments_partner endpoint=/hooks/inbound/payments algorithm=HMAC-SHA256";
    }

    private static string BuildExternalRateLimit(Random random)
    {
        return $"upstream_rate_limit host=search-provider.example status=429 retry_after_s={random.Next(2, 60)} quota_bucket=enterprise_tier";
    }

    private static string BuildDeprecationNoise(Random random)
    {
        return $"deprecated_route_called path=/api/v1/legacy-pricing sunset=2026-08-01 callers={random.Next(1, 40)}";
    }

    private static string BuildNoisyRetryLogged(Random random)
    {
        return $"retry_attempt_logged operation=fetch_product_catalog attempt={random.Next(2, 5)} outcome=will_retry backoff_ms={random.Next(50, 400)}";
    }

    private static string BuildBacklogLintDebt(Random random)
    {
        return $"static_analysis_debt module=PricingRules warnings={random.Next(3, 22)} baseline_only=true sprint_candidate=false";
    }

    private static string BuildDatabaseDeadlock(Random random)
    {
        var line = random.Next(20, 160);
        return "Transaction aborted during account update\\n" +
               "Npgsql.PostgresException (0x80004005): 40P01: deadlock detected\\n" +
               $"   at AccountRepository.UpdateBalanceAsync(Guid accountId, Decimal delta) in /src/Repositories/AccountRepository.cs:line {line}\\n" +
               "   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReaderAsync(...)";
    }

    private static string BuildKafkaProcessingError(Random random)
    {
        var line = random.Next(25, 220);
        return "Kafka consumer failed to process message\\n" +
               "Confluent.Kafka.ConsumeException: Broker: Unknown topic or partition\\n" +
               $"   at OrderEventsConsumer.HandleAsync(OrderEvent message) in /src/Messaging/OrderEventsConsumer.cs:line {line}\\n" +
               "   at BackgroundConsumer.ExecuteAsync(CancellationToken stoppingToken)";
    }

    private static string BuildLatencyP99Spike(Random random)
    {
        var ms = random.Next(2200, 9800);
        return $"latency_objective_breach dependency=InventoryApi observed_p99_ms={ms} slo_ms=1200 window=5m env=prod";
    }

    private static string BuildCacheStampede(Random random)
    {
        var keys = random.Next(800, 12000);
        return $"cache_stampede_risk hot_key_pattern=catalog:* concurrent_misses={keys} ttl_padding_applied=false";
    }

    private static string BuildCheckoutPaymentCritical(Random random)
    {
        var line = random.Next(55, 190);
        return "Payment capture declined during checkout\\n" +
               "PartnerPayments.PaymentDeclinedException: issuer_declined\\n" +
               $"   at PaymentOrchestrator.CaptureAsync(PaymentCapture cmd) in /src/Payments/PaymentOrchestrator.cs:line {line}\\n" +
               "   decline_code=05 AVS=n/a amount_currency=EUR";
    }

    private static string BuildHttpTimeoutException(Random random)
    {
        var line = random.Next(40, 180);
        return "Downstream dependency timeout\\n" +
               "System.TimeoutException: The HTTP request to BillingApi timed out after 10s.\\n" +
               $"   at BillingClient.CapturePaymentAsync(PaymentRequest request) in /src/Clients/BillingClient.cs:line {line}\\n" +
               "   at System.Net.Http.HttpClient.<SendAsync>g__Core|83_0(...)\\n" +
               "Inner exception: System.Net.Sockets.SocketException (110): Connection timed out";
    }

    private static string BuildCircuitBreakerOpen(Random random)
    {
        var openSeconds = random.Next(10, 90);
        return $"circuit_breaker_open dependency=NotificationProvider failures=8 open_seconds={openSeconds} last_status=503";
    }

    private static string BuildRetryExhausted(Random random)
    {
        var line = random.Next(35, 140);
        return "Retry policy exhausted after 3 attempts for webhook dispatch\\n" +
               $"   at WebhookDispatcher.DeliverAsync(WebhookPayload payload) in /src/Services/WebhookDispatcher.cs:line {line}\\n" +
               "last_status=408 endpoint=https://partner.example.com/webhook/orders";
    }
}
