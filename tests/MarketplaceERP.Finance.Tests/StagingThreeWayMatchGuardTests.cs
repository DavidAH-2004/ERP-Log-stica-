using MarketplaceERP.API.Services;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class StagingThreeWayMatchGuardTests
{
    private static readonly Guid AllowedInvoiceId =
        Guid.Parse("c256c1b1-ea2d-46c7-8c5f-891e893a946b");
    private static readonly Guid AllowedOrderId =
        Guid.Parse("a818da6b-360b-4248-9343-0359dba22d1b");

    [Fact]
    public void AllowsOnlyTheExplicitSyntheticInvoiceOnIsolatedStaging()
    {
        var context = CreateContext(
            new Validar3WayMatchRequest(AllowedOrderId, AllowedInvoiceId, 5),
            paymentsDisabled: true,
            Settings());

        new StagingThreeWayMatchGuardAttribute().OnActionExecuting(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void AllowsGeneralInvoiceAuditsOnlyWithExplicitIsolatedQaOptIn()
    {
        var settings = Settings();
        settings["Procurement:StagingThreeWayMatch:AllowIsolatedQaInvoices"] = "true";
        settings["Procurement:StagingThreeWayMatch:ExpiresAtUtc"] = "2020-01-01T00:00:00Z";
        settings["Procurement:StagingThreeWayMatch:InvoiceId"] = Guid.NewGuid().ToString();
        var approvedHost = "ep-qa-isolated.c-15.us-east-1.aws.neon.tech";
        var qaConnection = "Host=" + approvedHost
            + ";Port=5432;Database=marketplaceerp_test;Username=marketplaceerp_test_owner"
            + ";Password=fixture-only-secret;SslMode=VerifyFull;Channel Binding=Require";
        settings["CONNECTION_STRING"] = qaConnection;
        settings["ConnectionStrings:DefaultConnection"] = qaConnection;
        settings["Deployment:ApprovedStagingHost"] = approvedHost;

        var context = CreateContext(
            new Validar3WayMatchRequest(Guid.NewGuid(), Guid.NewGuid(), 3),
            paymentsDisabled: true,
            settings);

        new StagingThreeWayMatchGuardAttribute().OnActionExecuting(context);
        Assert.Null(context.Result);
    }

    [Fact]
    public void GeneralQaInvoiceAuditFailsClosedForAnyUnsafeTarget()
    {
        var settings = Settings();
        settings["Procurement:StagingThreeWayMatch:AllowIsolatedQaInvoices"] = "true";
        var approvedHost = "ep-qa-isolated.c-15.us-east-1.aws.neon.tech";
        var qaConnection = "Host=" + approvedHost
            + ";Port=5432;Database=marketplaceerp_test;Username=marketplaceerp_test_owner"
            + ";Password=fixture-only-secret;SslMode=VerifyFull;Channel Binding=Require";
        settings["CONNECTION_STRING"] = qaConnection;
        settings["ConnectionStrings:DefaultConnection"] = qaConnection;
        settings["Deployment:ApprovedStagingHost"] = approvedHost;
        var mismatched = new Validar3WayMatchRequest(Guid.NewGuid(), Guid.NewGuid(), 3);

        foreach (var key in new[]
        {
            "RENDER_SERVICE_NAME", "Deployment:Target", "Payments:Provider",
            "Deployment:ApprovedStagingHost", "CONNECTION_STRING",
            "ConnectionStrings:DefaultConnection"
        })
        {
            var unsafeConfig = new Dictionary<string, string?>(settings)
                { [key] = key == "RENDER_SERVICE_NAME" ? "marketplaceerp-api" : "UNSAFE" };
            var context = CreateContext(mismatched, paymentsDisabled: true, unsafeConfig);
            new StagingThreeWayMatchGuardAttribute().OnActionExecuting(context);
            AssertBlocked(context);
        }

        var enabledPayments = CreateContext(mismatched, paymentsDisabled: false, settings);
        new StagingThreeWayMatchGuardAttribute().OnActionExecuting(enabledPayments);
        AssertBlocked(enabledPayments);

        settings["Procurement:StagingThreeWayMatch:Enabled"] = "false";
        var disabledFeature = CreateContext(mismatched, paymentsDisabled: true, settings);
        new StagingThreeWayMatchGuardAttribute().OnActionExecuting(disabledFeature);
        AssertBlocked(disabledFeature);
    }

    [Fact]
    public void BlocksWhenNoExplicitAllowlistIsConfigured()
    {
        var context = CreateContext(
            new Validar3WayMatchRequest(AllowedOrderId, AllowedInvoiceId, 5),
            paymentsDisabled: true,
            new Dictionary<string, string?>());

        new StagingThreeWayMatchGuardAttribute().OnActionExecuting(context);

        AssertBlocked(context);
    }

    [Fact]
    public void BlocksDifferentInvoiceOrderOrReceivedQuantity()
    {
        var requests = new[]
        {
            new Validar3WayMatchRequest(AllowedOrderId, Guid.NewGuid(), 5),
            new Validar3WayMatchRequest(Guid.NewGuid(), AllowedInvoiceId, 5),
            new Validar3WayMatchRequest(AllowedOrderId, AllowedInvoiceId, 4)
        };

        foreach (var request in requests)
        {
            var context = CreateContext(request, paymentsDisabled: true, Settings());
            new StagingThreeWayMatchGuardAttribute().OnActionExecuting(context);
            AssertBlocked(context);
        }
    }

    [Fact]
    public void BlocksOutsideStagingOrWhenPaymentsAreNotDisabled()
    {
        var wrongService = Settings();
        wrongService["RENDER_SERVICE_NAME"] = "marketplaceerp-api";
        var wrongTarget = Settings();
        wrongTarget["Deployment:Target"] = "Production";
        var wrongProvider = Settings();
        wrongProvider["Payments:Provider"] = "Mock";

        foreach (var settings in new[] { wrongService, wrongTarget, wrongProvider })
        {
            var context = CreateContext(
                new Validar3WayMatchRequest(AllowedOrderId, AllowedInvoiceId, 5),
                paymentsDisabled: true,
                settings);
            new StagingThreeWayMatchGuardAttribute().OnActionExecuting(context);
            AssertBlocked(context);
        }

        var enabledPayments = CreateContext(
            new Validar3WayMatchRequest(AllowedOrderId, AllowedInvoiceId, 5),
            paymentsDisabled: false,
            Settings());
        new StagingThreeWayMatchGuardAttribute().OnActionExecuting(enabledPayments);
        AssertBlocked(enabledPayments);
    }

    [Fact]
    public void BlocksExpiredOneTimeAuthorization()
    {
        var settings = Settings();
        settings["Procurement:StagingThreeWayMatch:ExpiresAtUtc"] = "2020-01-01T00:00:00Z";

        var context = CreateContext(
            new Validar3WayMatchRequest(AllowedOrderId, AllowedInvoiceId, 5),
            paymentsDisabled: true,
            settings);
        new StagingThreeWayMatchGuardAttribute().OnActionExecuting(context);

        AssertBlocked(context);
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["RENDER_SERVICE_NAME"] = StagingDatabaseTargetGuard.ServiceName,
        ["Deployment:Target"] = StagingDatabaseTargetGuard.TargetMarker,
        ["Payments:Provider"] = "Disabled",
        ["Procurement:StagingThreeWayMatch:Enabled"] = "true",
        ["Procurement:StagingThreeWayMatch:InvoiceId"] = AllowedInvoiceId.ToString(),
        ["Procurement:StagingThreeWayMatch:OrderId"] = AllowedOrderId.ToString(),
        ["Procurement:StagingThreeWayMatch:ReceivedQuantity"] = "5",
        ["Procurement:StagingThreeWayMatch:ExpiresAtUtc"] =
            DateTimeOffset.UtcNow.AddHours(2).ToString("O")
    };

    private static ActionExecutingContext CreateContext(
        Validar3WayMatchRequest request,
        bool paymentsDisabled,
        IDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(new PaymentOperationsMode(paymentsDisabled))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(
            action,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?> { ["req"] = request },
            new object());
    }

    private static void AssertBlocked(ActionExecutingContext context)
    {
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("no-store", context.HttpContext.Response.Headers.CacheControl.ToString());
    }
}
