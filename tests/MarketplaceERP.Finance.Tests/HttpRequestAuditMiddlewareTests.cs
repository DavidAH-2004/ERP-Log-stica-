using MarketplaceERP.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class HttpRequestAuditMiddlewareTests
{
    private sealed class CaptureLogger : ILogger<HttpRequestAuditMiddleware>
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId,
            TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    [Theory]
    [InlineData("/health", "health.live")]
    [InlineData("/health/ready", "health.ready")]
    [InlineData("/api/auth/login", "auth.login")]
    [InlineData("/api/auth/session", "auth.session")]
    [InlineData("/api/auth/logout", "auth.logout")]
    [InlineData("/api/ordenes/secret-customer", "api.orders")]
    [InlineData("/api/WMS/abc", "api.other")]
    [InlineData("/api/productos/por-codigo/private-identifier", "barcode.lookup")]
    [InlineData("/api/productos/etiquetas/private-identifier", "barcode.labels")]
    [InlineData("/api/inventario/conteos-ciclicos/sesiones/private-identifier", "counts.sessions")]
    [InlineData("/api/productos/private-identifier", "api.catalog")]
    [InlineData("/api/inventario/private-identifier", "api.inventory")]
    [InlineData("/api/ordenes/private-identifier", "api.orders")]
    [InlineData("/api/compras/private-identifier", "api.procurement")]
    [InlineData("/api/finanzas/private-identifier", "api.finance")]
    [InlineData("/api/pagos/private-identifier", "api.payments")]
    [InlineData("/api/auth/private-identifier", "auth.other")]
    [InlineData("/api/productos/etiquetas", "barcode.labels")]
    [InlineData("/api/productos/por-codigo/4006381333931", "barcode.lookup")]
    [InlineData("/api/inventario/conteos-ciclicos/sesiones/11111111-1111-4111-8111-111111111111", "counts.sessions")]
    [InlineData("/swagger/index.html", "documentation")]
    [InlineData("/unrecognized-sensitive-info", "other")]
    public void RouteBucketsContainNoClientSuppliedPath(string path, string expected) =>
        Assert.Equal(expected, HttpRequestAuditMiddleware.ClassifyPath(new PathString(path)));

    [Fact]
    public async Task FailedAndAnonymousRequestsAreLoggedWithoutSecretsOrUserPath()
    {
        var logger = new CaptureLogger();
        var middleware = new HttpRequestAuditMiddleware(
            http =>
            {
                http.Response.StatusCode = 503;
                return Task.CompletedTask;
            }, logger);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/ordenes/customer@private.example";
        ctx.Request.QueryString = new QueryString("?jwt=private-value");
        ctx.Request.Method = "POST";
        ctx.Request.Headers.Authorization = "Bearer super-private-jwt";
        ctx.Request.Headers.Cookie = "session=private-cookie";
        await middleware.InvokeAsync(ctx);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("route=api.orders", entry.Text);
        Assert.Contains("method=POST", entry.Text);
        Assert.Contains("status=503", entry.Text);
        Assert.Contains("elapsed_ms=", entry.Text);
        Assert.DoesNotContain("customer@private", entry.Text);
        Assert.DoesNotContain("private-value", entry.Text);
        Assert.DoesNotContain("super-private-jwt", entry.Text);
        Assert.DoesNotContain("private-cookie", entry.Text);
    }

    [Theory]
    [InlineData("/api/productos/por-codigo/secret-ean-4006381333931", "barcode.lookup")]
    [InlineData("/api/inventario/conteos-ciclicos/sesiones/private-guid", "counts.sessions")]
    [InlineData("/api/compras/customer@private.example", "api.procurement")]
    public async Task F435_ErrorBucketIsConstantAndNeverLeaksPathData(string path, string expectedBucket)
    {
        var logger = new CaptureLogger();
        var middleware = new HttpRequestAuditMiddleware(
            http =>
            {
                http.Response.StatusCode = 503;
                return Task.CompletedTask;
            }, logger);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.QueryString = new QueryString("?token=private-query-token");
        ctx.Request.Headers.Authorization = "Bearer private-jwt";
        ctx.Request.Method = "GET";

        await middleware.InvokeAsync(ctx);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains($"route={expectedBucket}", entry.Text);
        Assert.Contains("status=503", entry.Text);
        Assert.DoesNotContain("private", entry.Text);
        Assert.DoesNotContain("4006381333931", entry.Text);
        Assert.DoesNotContain("customer@", entry.Text);
        Assert.DoesNotContain("token=", entry.Text);
    }

    [Fact]
    public async Task HealthReadinessIsLoggedWithFixedRouteAndNoPii()
    {
        var logger = new CaptureLogger();
        var middleware = new HttpRequestAuditMiddleware(
            http =>
            {
                http.Response.StatusCode = 200;
                return Task.CompletedTask;
            }, logger);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/health/ready";
        ctx.Request.Method = "GET";
        await middleware.InvokeAsync(ctx);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("route=health.ready", entry.Text);
        Assert.Contains("method=GET", entry.Text);
        Assert.Contains("status=200", entry.Text);
    }
}
