using FluentAssertions;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

public partial class SesionConteoServiceTests
{
    // ── Task 4: cerrar y cancelar ────────────────────────────────────────────

    private const string MensajeDecisiones = "Debes decidir cada producto que falta, una sola vez.";

    [Fact]
    public async Task Cerrar_ExigeDecisionPorCadaFaltante()
    {
        using var db = Db();
        var (_, s1) = await UnProducto(db);
        await UnProducto(db);
        var sesion = await Recorrido(db);
        db.ChangeTracker.Clear();

        var r = await Sut(db).CerrarAsync(sesion, new[] { new DecisionCierre(s1.Id, AccionCierre.Pendiente) }, Usuario);

        r.Exitoso.Should().BeFalse();
        r.Mensaje.Should().Be(MensajeDecisiones);
        db.ChangeTracker.Clear();
        (await db.SesionesConteo.SingleAsync()).Estado.Should().Be(EstadoSesionConteo.Abierta);
    }

    [Fact]
    public async Task Cerrar_DecisionParaItemNoFaltante_Falla()
    {
        using var db = Db();
        var (_, s1) = await UnProducto(db);
        var sesion = await Recorrido(db);
        db.ChangeTracker.Clear();

        var ajeno = await Sut(db).CerrarAsync(sesion, new[]
        {
            new DecisionCierre(s1.Id, AccionCierre.Pendiente),
            new DecisionCierre(Guid.NewGuid(), AccionCierre.NoHay),
        }, Usuario);
        var repetida = await Sut(db).CerrarAsync(sesion, new[]
        {
            new DecisionCierre(s1.Id, AccionCierre.Pendiente),
            new DecisionCierre(s1.Id, AccionCierre.NoHay),
        }, Usuario);

        ajeno.Exitoso.Should().BeFalse();
        repetida.Exitoso.Should().BeFalse();
        repetida.Mensaje.Should().Be(MensajeDecisiones);
        (await db.ConteosCiclicos.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Cerrar_NoHay_RegistraCeroYQuedaEnReconteo()
    {
        using var db = Db();
        var (_, s) = await UnProducto(db, total: 5);
        var sesion = await Recorrido(db);
        db.ChangeTracker.Clear();

        var r = await Sut(db).CerrarAsync(sesion, new[] { new DecisionCierre(s.Id, AccionCierre.NoHay) }, Usuario);

        r.Exitoso.Should().BeTrue();
        r.Sesion!.Estado.Should().Be(EstadoSesionConteo.Cerrada);
        r.Sesion.FechaCierre.Should().NotBeNull();
        var conteo = await ConteoActual(db, s.Id);
        conteo.Estado.Should().Be(EstadoConteoCiclico.RequiereReconteo);
        conteo.PrimerConteo.Should().Be(0);
    }

    [Fact]
    public async Task Cerrar_Pendiente_NoCreaConteo()
    {
        using var db = Db();
        var (_, s) = await UnProducto(db);
        var sesion = await Recorrido(db);
        db.ChangeTracker.Clear();

        var r = await Sut(db).CerrarAsync(sesion, new[] { new DecisionCierre(s.Id, AccionCierre.Pendiente) }, Usuario);

        r.Exitoso.Should().BeTrue();
        (await db.ConteosCiclicos.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Cerrar_SesionYaCerrada_Falla()
    {
        using var db = Db();
        var (_, s) = await UnProducto(db);
        var sesion = await Recorrido(db);
        db.ChangeTracker.Clear();
        var decisiones = new[] { new DecisionCierre(s.Id, AccionCierre.Pendiente) };
        await Sut(db).CerrarAsync(sesion, decisiones, Usuario);
        db.ChangeTracker.Clear();

        var r = await Sut(db).CerrarAsync(sesion, decisiones, Usuario);

        r.Exitoso.Should().BeFalse();
        r.Mensaje.Should().Be("La sesión ya no está abierta.");
    }

    [Fact]
    public async Task Cerrar_SesionInexistente_Lanza()
    {
        using var db = Db();
        await FluentActions.Invoking(() => Sut(db).CerrarAsync(Guid.NewGuid(), Array.Empty<DecisionCierre>(), Usuario))
            .Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Invoking(() => Sut(db).CancelarAsync(Guid.NewGuid(), Usuario))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Cancelar_DejaConteosComoEstan()
    {
        using var db = Db();
        var (p, s) = await UnProducto(db);
        var sesion = await Recorrido(db);
        await Leer(db, sesion, p.CodigoEAN, 3);
        db.ChangeTracker.Clear();

        var r = await Sut(db).CancelarAsync(sesion, Usuario);

        r.Exitoso.Should().BeTrue();
        r.Sesion!.Estado.Should().Be(EstadoSesionConteo.Cancelada);
        (await ConteoActual(db, s.Id)).Estado.Should().Be(EstadoConteoCiclico.RequiereReconteo);
    }

    [Fact]
    public async Task Cerrar_LiberaElPasillo_PermiteNuevoRecorrido()
    {
        using var db = Db();
        var (_, s) = await UnProducto(db);
        var sesion = await Recorrido(db);
        db.ChangeTracker.Clear();
        await Sut(db).CerrarAsync(sesion, new[] { new DecisionCierre(s.Id, AccionCierre.Pendiente) }, Usuario);
        db.ChangeTracker.Clear();

        (await Sut(db).AbrirRecorridoAsync("Pasillo A", Usuario)).Exitoso.Should().BeTrue();
    }
}
