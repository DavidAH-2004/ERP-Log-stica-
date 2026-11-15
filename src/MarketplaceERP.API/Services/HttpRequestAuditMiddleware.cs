using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace MarketplaceERP.API.Services;

/// <summary>
/// F367: provider-neutral HTTP latency/status telemetry. Never log URL paths,
/// query strings, authorization headers, cookies, user IDs or request bodies.
/// The route bucket is a constant chosen by code, never untrusted input.
/// </summary>
public sealed class HttpRequestAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HttpRequestAuditMiddleware> _logger;

    public HttpRequestAuditMiddleware(RequestDelegate next, ILogger<HttpRequestAuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var status = StatusCodes.Status500InternalServerError;
        try
        {
            await _next(context);
            status = context.Response.StatusCode;
        }
        finally
        {
            var elapsedMs = (long)Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            var bucket = ClassifyPath(context.Request.Path);
            var method = SafeMethod(context.Request.Method);
            // The only dynamic fields are numeric status/duration. Neither a
            // client-controlled URL nor an email/IP/token enters this event.
            if (status >= 500 || elapsedMs >= 3000)
                _logger.LogWarning(
                    "ERP_HTTP route={RouteBucket} method={MethodBucket} status={StatusCode} elapsed_ms={ElapsedMs}",
                    bucket, method, status, elapsedMs);
            else
                _logger.LogInformation(
                    "ERP_HTTP route={RouteBucket} method={MethodBucket} status={StatusCode} elapsed_ms={ElapsedMs}",
                    bucket, method, status, elapsedMs);
        }
    }

    public static string ClassifyPath(PathString path)
    {
        if (path.Equals(new PathString("/health"), StringComparison.OrdinalIgnoreCase))
            return "health.live";
        if (path.Equals(new PathString("/health/ready"), StringComparison.OrdinalIgnoreCase))
            return "health.ready";
        if (path.Equals(new PathString("/api/auth/login"), StringComparison.OrdinalIgnoreCase))
            return "auth.login";
        if (path.Equals(new PathString("/api/auth/session"), StringComparison.OrdinalIgnoreCase))
            return "auth.session";
        if (path.Equals(new PathString("/api/auth/logout"), StringComparison.OrdinalIgnoreCase))
            return "auth.logout";
        // F435: fixed route families make 5xx alerts actionable without
        // recording customer IDs, EANs, URL segments, cookies or tokens.
        if (path.StartsWithSegments(new PathString("/api/productos/por-codigo"), StringComparison.OrdinalIgnoreCase))
            return "barcode.lookup";
        if (path.StartsWithSegments(new PathString("/api/productos/etiquetas"), StringComparison.OrdinalIgnoreCase))
            return "barcode.labels";
        if (path.StartsWithSegments(new PathString("/api/inventario/conteos-ciclicos/sesiones"), StringComparison.OrdinalIgnoreCase))
            return "counts.sessions";
        if (path.StartsWithSegments(new PathString("/api/productos"), StringComparison.OrdinalIgnoreCase))
            return "api.catalog";
        if (path.StartsWithSegments(new PathString("/api/inventario"), StringComparison.OrdinalIgnoreCase))
            return "api.inventory";
        if (path.StartsWithSegments(new PathString("/api/ordenes"), StringComparison.OrdinalIgnoreCase))
            return "api.orders";
        if (path.StartsWithSegments(new PathString("/api/compras"), StringComparison.OrdinalIgnoreCase))
            return "api.procurement";
        if (path.StartsWithSegments(new PathString("/api/finanzas"), StringComparison.OrdinalIgnoreCase))
            return "api.finance";
        if (path.StartsWithSegments(new PathString("/api/pagos"), StringComparison.OrdinalIgnoreCase))
            return "api.payments";
        if (path.StartsWithSegments(new PathString("/api/auth"), StringComparison.OrdinalIgnoreCase))
            return "auth.other";
        if (path.StartsWithSegments(new PathString("/api"), StringComparison.OrdinalIgnoreCase))
            return "api.other";
        if (path.StartsWithSegments(new PathString("/swagger"), StringComparison.OrdinalIgnoreCase))
            return "documentation";
        return "other";
    }

    private static string SafeMethod(string method) => method.ToUpperInvariant() switch
    {
        "GET" => "GET",
        "POST" => "POST",
        "PUT" => "PUT",
        "PATCH" => "PATCH",
        "DELETE" => "DELETE",
        "HEAD" => "HEAD",
        "OPTIONS" => "OPTIONS",
        _ => "OTHER"
    };
}
