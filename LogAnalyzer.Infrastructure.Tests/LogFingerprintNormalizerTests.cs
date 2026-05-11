using LogAnalyzer.Infrastructure.Services;

namespace LogAnalyzer.Infrastructure.Tests;

public sealed class LogFingerprintNormalizerTests
{
    [Fact]
    public void Normalize_collapses_mock_style_timestamps_and_correlation_ids()
    {
        var a =
            "[6] 2026-05-10T12:00:01.456Z INFO [WebBff] eval_severity=info trace_id=aaaaaaaaaaaaaaaa span_id=bbbbbbbbbbbbbbbb request_id=req-cccccccc event=\"request_completed_ms=120 status=200 cache_hit=true rows=5\"";

        var b =
            "[6] 2026-05-10T18:30:44.000Z INFO [WebBff] eval_severity=info trace_id=dddddddddddddddd span_id=eeeeeeeeeeeeeeee request_id=req-ffffffff event=\"request_completed_ms=120 status=200 cache_hit=true rows=5\"";

        var na = LogFingerprintNormalizer.NormalizeForStableFingerprint(a);
        var nb = LogFingerprintNormalizer.NormalizeForStableFingerprint(b);

        Assert.Equal(na, nb);

        Assert.DoesNotContain("aaaaaaaaaaaaaaaa", na, StringComparison.Ordinal);
        Assert.Contains("trace_id=*", na, StringComparison.Ordinal);
        Assert.Contains("request_completed_ms=", na, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_preserves_distinct_event_payloads()
    {
        var timeout =
            "[3] 2026-05-10T12:00:00.000Z ERROR [OrderService] trace_id=a span_id=b request_id=req-x event=\"timeout host=a\"";
        var deadlock =
            "[3] 2026-05-10T12:00:01.000Z ERROR [OrderService] trace_id=c span_id=d request_id=req-y event=\"deadlock\"";

        var nt = LogFingerprintNormalizer.NormalizeForStableFingerprint(timeout);
        var nd = LogFingerprintNormalizer.NormalizeForStableFingerprint(deadlock);

        Assert.NotEqual(nt, nd);
    }

    [Fact]
    public void Sha256LogFingerprintService_stable_matches_across_volatile_fields()
    {
        var svc = new Sha256LogFingerprintService();
        var a =
            "[6] 2026-05-10T12:00:01.001Z WARN [BillingService] trace_id=111 span_id=222 request_id=req-333 event=\"same\"";
        var b =
            "[6] 2026-05-11T09:15:00.999Z WARN [BillingService] trace_id=444 span_id=555 request_id=req-666 event=\"same\"";

        Assert.Equal(svc.ComputeStableHash(a), svc.ComputeStableHash(b));
        Assert.NotEqual(svc.ComputeHash(a), svc.ComputeHash(b));
    }
}
