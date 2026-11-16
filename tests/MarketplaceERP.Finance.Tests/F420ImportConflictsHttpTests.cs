using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>F420: conflicto de negocio por CSV = HTTP 409, no HTTP 500.</summary>
public sealed class F420ImportConflictsHttpTests
{
    [Fact]
    public async Task CsvAdmin_EanDuplicado_Devuelve409SinModificarDb()
    {
        await using var db = new CatalogDbContext(
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseInMemoryDatabase("F420-" + Guid.NewGuid()).Options);
        var importer = new Mock<ICatalogImportService>();
        importer.Setup(i => i.ImportarCatalogoCsvAsync(
                It.IsAny<Stream>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CodigoEanDuplicadoException("F420-TEST"));
        var controller = new ProductosController(
            db, importer.Object, NullLogger<ProductosController>.Instance);
        using var bytes = new MemoryStream(new byte[] { 1, 2, 3 });
        var form = new FormFile(bytes, 0, bytes.Length, "archivo", "catalogo.csv");

        var response = Assert.IsType<ConflictObjectResult>(await controller.ImportarCsv(form));
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.DoesNotContain("4006381333931", response.Value?.ToString() ?? "");
        Assert.Equal(0, await db.Productos.CountAsync());
        Assert.Equal(0, await db.StockFisico.CountAsync());
    }
}
