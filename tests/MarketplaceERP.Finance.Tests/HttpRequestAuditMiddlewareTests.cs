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
    [InlineData("/api/ordenes/secret-customer", "api.other")]
    [InlineData("/api/WMS/abc", "api.other")]
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
        Assert.Contains("route=api.other", entry.Text);
        Assert.Contains("method=POST", entry.Text);
        Assert.Contains("status=503", entry.Text);
        Assert.Contains("elapsed_ms=", entry.Text);
        Assert.DoesNotContain("customer@private", entry.Text);
        Assert.DoesNotContain("private-value", entry.Text);
        Assert.DoesNotContain("super-private-jwt", entry.Text);
        Assert.DoesNotContain("private-cookie", entry.Text);
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
