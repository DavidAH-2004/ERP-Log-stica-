using MarketplaceERP.Catalog.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations;

/// <summary>
/// F419: garantiza que no existan dos productos con el mismo identificador EAN
/// no vacío. Se niega a migrar si hay duplicados históricos; NUNCA corrige
/// códigos existentes ni cambia inventario, reservas o precios.
/// </summary>
[DbContext(typeof(CatalogDbContext))]
[Migration("20261115183000_EanNoVacioUnico")]
public partial class EanNoVacioUnico : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM catalog.productos
                    WHERE "CodigoEAN" IS NOT NULL AND "CodigoEAN" <> ''
                    GROUP BY "CodigoEAN"
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION
                        'F419: EAN historicos duplicados; detener migracion y conciliar sin renumerar etiquetas';
                END IF;
            END $$;

            CREATE UNIQUE INDEX "UX_productos_CodigoEAN_no_vacio"
                ON catalog.productos ("CodigoEAN")
                WHERE "CodigoEAN" IS NOT NULL AND "CodigoEAN" <> '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reversión de DDL exclusivamente, ninguna mutación del catálogo.
        migrationBuilder.Sql("""
            DROP INDEX catalog."UX_productos_CodigoEAN_no_vacio";
            """);
    }
}
