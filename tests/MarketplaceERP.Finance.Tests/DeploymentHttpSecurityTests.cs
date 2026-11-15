using System.Net;
using MarketplaceERP.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class DeploymentHttpSecurityTests
{
    [Theory]
    [InlineData("marketplaceerp-api-staging", true)]
    [InlineData("marketplaceerp-api", true)]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    [InlineData("marketplaceerp-api-unknown", false)]
    [InlineData("other-render-service", false)]
    [InlineData("MARKETPLACEERP-API", false)]
    public void Render_UsaTerminacionTlsDelProveedor(string? nombre, bool esperado) =>
        Assert.Equal(esperado, DeploymentHttpSecurity.RenderTerminatesTls(nombre));

    [Fact]
    public void Forwarding_NoConfiaEnInternetNiEnRedesAbiertas()
    {
        var options = DeploymentHttpSecurity.LocalTrustedForwarders();
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Empty(options.KnownNetworks);
        Assert.Equal(2, options.KnownProxies.Count);
        Assert.Contains(IPAddress.Loopback, options.KnownProxies);
        Assert.Contains(IPAddress.IPv6Loopback, options.KnownProxies);
        Assert.DoesNotContain(IPAddress.Any, options.KnownProxies);
        Assert.DoesNotContain(IPAddress.IPv6Any, options.KnownProxies);
    }

    [Fact]
    public async Task ProxyDesconocido_NoPuedeFalsificarIpNiEsquema()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.25");
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.50";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(DeploymentHttpSecurity.LocalTrustedForwarders()));
        await middleware.Invoke(context);

        Assert.Equal(IPAddress.Parse("198.51.100.25"), context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task ProxyLoopbackVerificado_SigueFuncionando()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.52";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(DeploymentHttpSecurity.LocalTrustedForwarders()));
        await middleware.Invoke(context);

        Assert.Equal(IPAddress.Parse("203.0.113.52"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public void HstsRender_SinIncluirSubdominiosYNoReemplazaPoliticaExistente()
    {
        var headers = new HeaderDictionary();
        DeploymentHttpSecurity.AddRenderHsts(headers);
        Assert.Equal("max-age=2592000", headers["Strict-Transport-Security"].ToString());

        headers["Strict-Transport-Security"] = "max-age=3600";
        DeploymentHttpSecurity.AddRenderHsts(headers);
        Assert.Equal("max-age=3600", headers["Strict-Transport-Security"].ToString());
    }
    [Theory]
    [InlineData("marketplaceerp-api")]
    [InlineData("marketplaceerp-api-staging")]
    public void F388_RenderConfiaSoloEnIpSobrescritaPorCloudflare(string renderService)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.11");
        context.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 203.0.113.99";
        context.Request.Headers["CF-Connecting-IP"] = "8.8.8.8";
        Assert.Equal("8.8.8.8",
            DeploymentHttpSecurity.LoginRateLimitPartition(context, renderService));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("other-service")]
    [InlineData("marketplaceerp-api-dev-unknown")]
    public void F388_SinRenderConfiableIgnoraCFYForwardedFor(string? renderService)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.12");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.11";
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.12";
        Assert.Equal("198.51.100.12",
            DeploymentHttpSecurity.LoginRateLimitPartition(context, renderService));
    }

    [Theory]
    [InlineData("198.51.100.14, 203.0.113.20")]
    [InlineData("invalid-client")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("10.10.0.7")]
    [InlineData("172.16.1.5")]
    [InlineData("192.168.1.99")]
    [InlineData("169.254.12.4")]
    [InlineData("100.64.12.4")]
    [InlineData("192.0.0.8")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.254")]
    [InlineData("192.0.2.10")]
    [InlineData("198.51.100.10")]
    [InlineData("203.0.113.10")]
    [InlineData("2001:db8::1")]
    [InlineData("224.0.0.1")]
    [InlineData("fc00::123")]
    [InlineData("fe80::123")]
    [InlineData("::ffff:10.0.0.25")]
    public void F388_CabeceraMalformadaOInseguraNoControlaLimitador(string value)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.13");
        context.Request.Headers["CF-Connecting-IP"] = value;
        Assert.Equal("198.51.100.13",
            DeploymentHttpSecurity.LoginRateLimitPartition(context, "marketplaceerp-api-staging"));
    }

    [Fact]
    public void F388_CabeceraRepetidaEsRechazada()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.15");
        context.Request.Headers["CF-Connecting-IP"] =
            new Microsoft.Extensions.Primitives.StringValues(new[] { "203.0.113.1", "203.0.113.2" });
        Assert.Equal("198.51.100.15",
            DeploymentHttpSecurity.LoginRateLimitPartition(context, "marketplaceerp-api"));
    }

    [Fact]
    public void F388_NoCabeceraUsaPeerYPreservaCompatibilidadLocal()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.35");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.100";
        Assert.Equal("192.0.2.35",
            DeploymentHttpSecurity.LoginRateLimitPartition(context, "marketplaceerp-api"));
    }

}
