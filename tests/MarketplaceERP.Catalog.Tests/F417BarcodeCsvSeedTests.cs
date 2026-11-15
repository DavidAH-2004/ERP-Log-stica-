using System.Text;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.SeedData;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>
/// F417: el catálogo histórico puede contener EAN sintéticos no conformes;
/// no se convierten automáticamente en identificadores operacionales.
/// </summary>
public sealed class F417BarcodeCsvSeedTests
{
    private const string Header =
        "\"SKU\",\"Nombre del Producto\",\"Categoría\",\"Subcategoría\",\"Precio B2C ($)\",\"Precio B2B ($)\",\"Stock Inicial\",\"Stock Mínimo\",\"Proveedor\",\"País Origen\",\"Peso (kg)\",\"Dimensiones (cm)\",\"Unidad de Medida\",\"Estado\",\"Código EAN\",\"Descripción Corta\"";

    private static string Row(string sku, string ean) =>
        $"\"{sku}\",\"Producto de prueba\",\"Electrónica y Tecnología\",\"General\",\"25.00\",\"20.00\",\"12\",\"4\",\"Proveedor\",\"El Salvador\",\"1.00\",\"20x10x5 cm\",\"Unidad\",\"Activo\",\"{ean}\",\"Ficha\"";

    [Fact]
    public async Task F417_AltaNueva_NoPersisteEanInvalidoNiInventaIdentificador()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"F417Seed_{Guid.NewGuid():N}").Options;
        await using var db = new CatalogDbContext(options);
        var service = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(Header + "\n" +
            Row("F417-INVALID", "7410000000001") + "\n" +
            Row("F417-VALID", "4006381333931") + "\n" +
            Row("F417-EMPTY", "") + "\n" +
            Row("F417-RESERVED", CodigoBarras.GenerarInterno(7)) + "\n"));

        var result = await service.ImportarCatalogoCsvAsync(csv, Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(4, result.Exitosos);
        Assert.Equal(0, result.Errores);

        var products = await db.Productos.AsNoTracking().ToDictionaryAsync(p => p.Sku);
        Assert.Null(products["F417-INVALID"].CodigoEAN);
        Assert.Equal("4006381333931", products["F417-VALID"].CodigoEAN);
        Assert.Null(products["F417-EMPTY"].CodigoEAN);
        Assert.Null(products["F417-RESERVED"].CodigoEAN);
        Assert.Equal(4, await db.StockFisico.CountAsync());
    }

    [Fact]
    public void F417_GeneradorDemo_DejaEanVacioParaAsignacionAdmin()
    {
        var file = Path.Combine(Path.GetTempPath(), $"f417_catalog_{Guid.NewGuid():N}.csv");
        try
        {
            CatalogDataGenerator.GenerarCsv(file);
            var rows = File.ReadAllLines(file);
            Assert.Equal(501, rows.Length); // encabezado + 500 SKUs
            Assert.All(rows.Skip(1), line =>
                Assert.True(line.Contains("\"Activo\",\"\",\"", StringComparison.Ordinal) ||
                            line.Contains("\"Inactivo\",\"\",\"", StringComparison.Ordinal),
                    "Los datos de ejemplo no deben atribuir códigos 741/GS1 ni generar EAN sintéticos"));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
