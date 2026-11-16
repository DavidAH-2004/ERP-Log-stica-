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

    // F445: only known Render services may skip Kestrel redirection and
    // depend on the external TLS terminator. A spurious environment name
    // must not silently disable HTTPS enforcement in another deployment.
    public static bool RenderTerminatesTls(string? renderServiceName) =>
        renderServiceName is "marketplaceerp-api" or "marketplaceerp-api-staging";

    /// <summary>
    /// F458: service name alone does NOT authenticate a request header.
    /// Until the actual ingress topology and CF-Connecting-IP overwriting have
    /// been independently verified, use only the connection's peer address.
    /// An explicit opt-in is available for a verified edge; never trust XFF.
    /// For Vercel traffic the peer may be a proxy rather than the browser.
    /// This local partition is NOT a distributed per-browser rate limit.
    /// </summary>
    public static string LoginRateLimitPartition(
        HttpContext context, string? renderServiceName, bool confiarCabeceraCfVerificada = false)
    {
        var direct = context.Connection.RemoteIpAddress;
        var fallback = direct?.IsIPv4MappedToIPv6 == true
            ? direct.MapToIPv4().ToString()
            : direct?.ToString() ?? "unknown";

        // An arbitrary client may supply a plausible CF header. No header can
        // partition login attempts unless the operator has verified rewriting
        // by the trusted Render ingress for the exact service.
        if (!confiarCabeceraCfVerificada ||
            renderServiceName is not ("marketplaceerp-api" or "marketplaceerp-api-staging"))
            return fallback;

        var values = context.Request.Headers["CF-Connecting-IP"];
        // Multiple headers, comma-joined chains, malformed addresses, loopback
        // and wildcards must never influence the limiter key.
        if (values.Count != 1 || values[0] is not string raw ||
            raw.Contains(',') || !IPAddress.TryParse(raw, out var ip))
            return fallback;

        ip = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
        // F442: a public ingress cannot legitimately report an RFC1918,
        // link-local, unspecified or multicast browser IP. Reject such
        // values instead of letting an untrusted/private key partition the
        // authentication limiter. No X-Forwarded-For trust is added.
        if (IsNonPublicRateLimitAddress(ip)) return fallback;
        return ip.ToString();
    }

    private static bool IsNonPublicRateLimitAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) ||
            ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal ||
            ip.IsIPv6Multicast || ip.IsIPv6SiteLocal)
            return true;

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var a = ip.GetAddressBytes();
            // F453: unique-local and IPv6 documentation prefix are not public clients.
            return (a[0] & 0xfe) == 0xfc ||
                   (a[0] == 0x20 && a[1] == 0x01 && a[2] == 0x0d && a[3] == 0xb8);
        }

        var b = ip.GetAddressBytes();
        return b[0] == 0 || b[0] == 10 || b[0] >= 224 ||
               (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
               (b[0] == 192 && b[1] == 168) ||
               (b[0] == 169 && b[1] == 254) ||
               (b[0] == 100 && b[1] >= 64 && b[1] <= 127) ||
               // F450: protocol assignments and benchmarking addresses
               // are not valid routable client IPs for login partitioning.
               (b[0] == 192 && b[1] == 0 && b[2] == 0) ||
               (b[0] == 198 && (b[1] == 18 || b[1] == 19)) ||
               // RFC 5737 TEST-NET ranges must not appear as real ingress clients.
               (b[0] == 192 && b[1] == 0 && b[2] == 2) ||
               (b[0] == 198 && b[1] == 51 && b[2] == 100) ||
               (b[0] == 203 && b[1] == 0 && b[2] == 113);
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
