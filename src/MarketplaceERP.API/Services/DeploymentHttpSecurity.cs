using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;

namespace MarketplaceERP.API.Services;

/// <summary>
/// Render termina TLS en su balanceador y redirige HTTP externamente.
/// No se deben aceptar X-Forwarded-* de orígenes arbitrarios: la IP resultante
/// también se utiliza para limitar intentos de inicio de sesión.
/// </summary>
public static class DeploymentHttpSecurity
{
    // HSTS para el dominio HTTPS de la API; sin includeSubDomains ni preload.
    public const string RenderHsts = "max-age=2592000";

    public static bool RenderTerminatesTls(string? renderServiceName) =>
        !string.IsNullOrWhiteSpace(renderServiceName);

    /// <summary>
    /// F388: Render's public ingress overwrites CF-Connecting-IP with the
    /// Cloudflare-observed peer IP. Never trust arbitrary X-Forwarded-For,
    /// Vercel request headers, or the same CF header on a non-Render host.
    /// A direct Render client and an upstream Vercel proxy are distinct:
    /// for Vercel traffic this IP identifies Vercel's egress, NOT the browser.
    /// Until a signed edge identity exists, this is an in-memory best-effort
    /// partition, not a distributed per-browser rate limit.
    /// </summary>
    public static string LoginRateLimitPartition(HttpContext context, string? renderServiceName)
    {
        var direct = context.Connection.RemoteIpAddress;
        var fallback = direct?.IsIPv4MappedToIPv6 == true
            ? direct.MapToIPv4().ToString()
            : direct?.ToString() ?? "unknown";

        if (renderServiceName is not ("marketplaceerp-api" or "marketplaceerp-api-staging"))
            return fallback;

        var values = context.Request.Headers["CF-Connecting-IP"];
        // Multiple headers, comma-joined chains, malformed addresses, loopback
        // and wildcards must never influence the limiter key.
        if (values.Count != 1 || values[0] is not string raw ||
            raw.Contains(',') || !IPAddress.TryParse(raw, out var ip) ||
            IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) ||
            ip.Equals(IPAddress.IPv6Any))
            return fallback;

        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }

    /// <summary>
    /// Políticas de proxies para hosts que NO están en Render.
    /// Sólo el loopback está autorizado de forma predeterminada. Si otra
    /// plataforma necesita proxies adicionales, se deben registrar por IP/CIDR
    /// verificados en un cambio explícito, nunca confiar globalmente.
    /// </summary>
    public static ForwardedHeadersOptions LocalTrustedForwarders()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };

        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
        return options;
    }

    /// <summary>
    /// El navegador ve HTTPS en Render aunque el salto proxy→Kestrel sea HTTP.
    /// No sobrescribir políticas HSTS existentes agregadas por el proveedor.
    /// </summary>
    public static void AddRenderHsts(IHeaderDictionary headers) =>
        headers.TryAdd("Strict-Transport-Security", RenderHsts);
}
