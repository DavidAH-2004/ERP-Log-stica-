using MarketplaceERP.Catalog.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations;

/// <summary>
/// F402: migración estrictamente ADITIVA. No borra, vacía, trunca ni
/// renumera CodigoEAN. Nunca copiar el UPDATE destructivo del fork.
/// </summary>
[DbContext(typeof(CatalogDbContext))]
[Migration("20261115072000_CodigosBarrasInternosSeguros")]
public partial class CodigosBarrasInternosSeguros : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE SEQUENCE catalog.codigos_barras_consecutivo
                AS bigint INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999999
                START WITH 1 NO CYCLE;

            DO $$
            DECLARE maximo bigint;
            BEGIN
                SELECT COALESCE(MAX(substring("CodigoEAN" FROM 3 FOR 10)::bigint), 0)
                  INTO maximo
                  FROM catalog.productos
                 WHERE "CodigoEAN" ~ '^20[0-9]{11}$';

                -- Nunca reciclar consecutivos preexistentes aunque
                -- el dígito verificador legado fuera incorrecto.
                PERFORM setval('catalog.codigos_barras_consecutivo'::regclass,
                    GREATEST(1, maximo), maximo > 0);
            END $$;

            -- Falla cerrado si ya existen identificadores internos duplicados.
            CREATE UNIQUE INDEX "UX_productos_CodigoEAN_interno"
                ON catalog.productos ("CodigoEAN")
                WHERE "CodigoEAN" ~ '^20[0-9]{11}$';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM catalog.productos
                    WHERE "CodigoEAN" ~ '^20[0-9]{11}$'
                ) THEN
                    RAISE EXCEPTION 'Rollback bloqueado: hay codigos internos asignados; no se pueden reutilizar.';
                END IF;
            END $$;

            DROP INDEX catalog."UX_productos_CodigoEAN_interno";
            DROP SEQUENCE catalog.codigos_barras_consecutivo;
            """);
    }
}
