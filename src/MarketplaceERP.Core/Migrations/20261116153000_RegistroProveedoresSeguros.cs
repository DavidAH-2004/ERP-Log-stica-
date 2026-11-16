using MarketplaceERP.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Core.Migrations;

/// <summary>
/// F660: single corporate supplier master. New OTIF is unknown, never a
/// synthetic percentage; enforce concurrent uniqueness without rewriting
/// existing identifiers, names, or historical measurements.
/// </summary>
[DbContext(typeof(CoreDbContext))]
[Migration("20261116153000_RegistroProveedoresSeguros")]
public sealed class RegistroProveedoresSeguros : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE core.proveedores
              ALTER COLUMN "OtifHistorico" DROP DEFAULT,
              ALTER COLUMN "OtifHistorico" DROP NOT NULL;
            """);

        // PostgreSQL will reject this migration if preexisting duplicate
        // business identifiers exist: never silently merge or delete vendors.
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "UX_proveedores_nombre_normalizado"
              ON core.proveedores (lower(regexp_replace(btrim("NombreComercial"), '[[:space:]]+', ' ', 'g')));
            CREATE UNIQUE INDEX "UX_proveedores_razon_normalizada"
              ON core.proveedores (lower(regexp_replace(btrim("RazonSocial"), '[[:space:]]+', ' ', 'g')));
            CREATE UNIQUE INDEX "UX_proveedores_nit_normalizado"
              ON core.proveedores (regexp_replace("Nit", '[[:space:]-]+', '', 'g'))
              WHERE btrim("Nit") <> '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS core."UX_proveedores_nit_normalizado";
            DROP INDEX IF EXISTS core."UX_proveedores_razon_normalizada";
            DROP INDEX IF EXISTS core."UX_proveedores_nombre_normalizado";
            """);
        // A supplier without measured OTIF cannot be rolled back to NOT NULL
        // without falsifying data. Block that rollback rather than write 95%.
        migrationBuilder.Sql("""
            DO $$
            BEGIN
              IF EXISTS (SELECT 1 FROM core.proveedores WHERE "OtifHistorico" IS NULL) THEN
                RAISE EXCEPTION 'Rollback blocked: suppliers without historical OTIF exist.';
              END IF;
            END $$;
            ALTER TABLE core.proveedores
              ALTER COLUMN "OtifHistorico" SET NOT NULL,
              ALTER COLUMN "OtifHistorico" SET DEFAULT 95.0;
            """);
    }
}
