using FluentAssertions;
using MarketplaceERP.Catalog.Services;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

public class CodigoBarrasTests
{
    [Theory]
    [InlineData("200000000001", '5')]
    [InlineData("741000000019", '2')]   // seed ALM-0001: el actual termina en 1 (inválido)
    [InlineData("400638133393", '1')]   // EAN público conocido 4006381333931
    public void CalcularDigitoVerificador_Ejemplos(string doce, char esperado)
        => CodigoBarras.CalcularDigitoVerificador(doce).Should().Be(esperado);

    [Theory]
    [InlineData("20000000000")]
    [InlineData("2000000000011")]
    [InlineData("20000000000A")]
    public void CalcularDigitoVerificador_EntradaInvalida_Lanza(string entrada)
        => FluentActions.Invoking(() => CodigoBarras.CalcularDigitoVerificador(entrada))
            .Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("2000000000015", true)]
    [InlineData("4006381333931", true)]
    [InlineData("7410000000191", false)] // verificador incorrecto (dato del seed)
    [InlineData("200000000001", false)]  // 12 dígitos
    [InlineData("20000000000150", false)]
    [InlineData("20000000000A5", false)]
    [InlineData("２000000000015", false)] // dígito Unicode de ancho completo
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EsEan13Valido_Casos(string? codigo, bool esperado)
        => CodigoBarras.EsEan13Valido(codigo).Should().Be(esperado);

    [Fact]
    public void EsEan13Valido_ConEspacios_FalsoSinRecortar()
        => CodigoBarras.EsEan13Valido(" 2000000000015 ").Should().BeFalse();

    [Fact]
    public void GenerarInterno_Uno() => CodigoBarras.GenerarInterno(1).Should().Be("2000000000015");

    [Fact]
    public void GenerarInterno_Maximo_EsValidoEInterno()
    {
        var c = CodigoBarras.GenerarInterno(CodigoBarras.ConsecutivoMaximo);
        c.Should().StartWith("209999999999");
        CodigoBarras.EsInterno(c).Should().BeTrue();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(10_000_000_000L)]
    public void GenerarInterno_FueraDeRango_Lanza(long n)
        => FluentActions.Invoking(() => CodigoBarras.GenerarInterno(n)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void EsInterno_EanExternoValido_Falso() => CodigoBarras.EsInterno("4006381333931").Should().BeFalse();

    [Fact]
    public void ExtraerConsecutivo_IdaYVuelta()
        => CodigoBarras.ExtraerConsecutivo(CodigoBarras.GenerarInterno(4321)).Should().Be(4321);

    [Fact]
    public void ExtraerConsecutivo_NoInterno_Lanza()
        => FluentActions.Invoking(() => CodigoBarras.ExtraerConsecutivo("4006381333931"))
            .Should().Throw<ArgumentException>();
}
