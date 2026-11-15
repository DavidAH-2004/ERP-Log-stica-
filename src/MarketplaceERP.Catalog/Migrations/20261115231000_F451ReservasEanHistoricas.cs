using MarketplaceERP.Catalog.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations;

/// <summary>
/// F451: add non-reusable EAN reservations and append-only rotation audit scaffolding.
/// Backfills only checksum-valid existing codes; DOES NOT rotate identifiers.
/// Must be tested on disposable PostgreSQL before authorizing a DEV migration.
/// </summary>
[DbContext(typeof(CatalogDbContext))]
[Migration("20261115231000_F451ReservasEanHistoricas")]
public partial class F451ReservasEanHistoricas : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE catalog.reservas_ean (
                "CodigoEAN" varchar(13) PRIMARY KEY,
                "ProductoId" uuid NOT NULL REFERENCES catalog.productos("Id") ON DELETE RESTRICT,
                "Estado" varchar(16) NOT NULL DEFAULT 'Actual',
                "Origen" varchar(12) NOT NULL DEFAULT 'Emision',
                "CreadoUtc" timestamptz NOT NULL DEFAULT now(),
                "UltimaTransicionUtc" timestamptz NULL,
                CONSTRAINT "CK_reservas_ean_codigo" CHECK ("CodigoEAN" ~ '^[0-9]{13}$'),
                CONSTRAINT "CK_reservas_ean_estado" CHECK ("Estado" IN ('Actual','Alias','Retirado')),
                CONSTRAINT "CK_reservas_ean_origen" CHECK ("Origen" IN ('Backfill','Emision'))
            );
            CREATE UNIQUE INDEX "UX_reservas_ean_actual_producto"
                ON catalog.reservas_ean ("ProductoId") WHERE "Estado" = 'Actual';

            CREATE TABLE catalog.rotaciones_ean (
                "Id" uuid PRIMARY KEY,
                "SolicitudId" uuid NOT NULL,
                "ProductoId" uuid NOT NULL REFERENCES catalog.productos("Id") ON DELETE RESTRICT,
                "CodigoAnterior" varchar(50) NOT NULL,
                "CodigoNuevo" varchar(13) NOT NULL,
                "Motivo" text NOT NULL,
                "SolicitadoPor" uuid NOT NULL,
                "AprobadoPor" uuid NULL,
                "SolicitadoUtc" timestamptz NOT NULL DEFAULT now(),
                "AprobadoUtc" timestamptz NULL,
                "EjecutadoUtc" timestamptz NULL,
                "Version" integer NOT NULL DEFAULT 1,
                "Estado" varchar(24) NOT NULL DEFAULT 'Solicitada',
                CONSTRAINT "CK_rotaciones_ean_estado"
                    CHECK ("Estado" IN ('Solicitada','Aprobada','Ejecutada','Rechazada','Fallida')),
                CONSTRAINT "CK_rotaciones_ean_version" CHECK ("Version" > 0),
                CONSTRAINT "UQ_rotaciones_ean_solicitud_version" UNIQUE ("SolicitudId", "Version"),
                CONSTRAINT "CK_rotaciones_ean_motivo" CHECK (char_length(trim("Motivo")) BETWEEN 1 AND 1000),
                CONSTRAINT "CK_rotaciones_ean_codigo" CHECK ("CodigoNuevo" ~ '^[0-9]{13}$')
            );
            CREATE INDEX "IX_rotaciones_ean_producto_fecha"
                ON catalog.rotaciones_ean ("ProductoId", "SolicitadoUtc");
            CREATE INDEX "IX_rotaciones_ean_codigo_anterior"
                ON catalog.rotaciones_ean ("CodigoAnterior");

            -- An existing legacy invalid code is NOT promoted to a scannable alias.
            -- The pre-existing F419 unique index rejects duplicate nonempty codes.
            INSERT INTO catalog.reservas_ean ("CodigoEAN", "ProductoId", "Estado", "Origen")
            SELECT p."CodigoEAN", p."Id", 'Actual', 'Backfill'
            FROM catalog.productos AS p
            WHERE p."CodigoEAN" ~ '^[0-9]{13}$'
              AND (
                  (10 - (
                      (SELECT sum(substr(p."CodigoEAN", n, 1)::integer *
                                   CASE WHEN n % 2 = 0 THEN 3 ELSE 1 END)
                       FROM generate_series(1, 12) AS n) % 10
                  )) % 10
              ) = substr(p."CodigoEAN", 13, 1)::integer;

            -- Assignment through the existing service, imports and other writers
            -- must reserve every newly issued valid EAN in the SAME SQL statement.
            -- A removed or changed historical code is not freed for another SKU.
            CREATE FUNCTION catalog.f451_reservar_ean_producto() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            DECLARE
                existente uuid;
                estado_actual varchar(16);
            BEGIN
                IF TG_OP = 'UPDATE' THEN
                    IF OLD."CodigoEAN" IS NOT NULL AND OLD."CodigoEAN" <> ''
                       AND OLD."CodigoEAN" IS DISTINCT FROM NEW."CodigoEAN" THEN
                        RAISE EXCEPTION 'F451: CodigoEAN historico inmutable; usar rotacion auditada'
                            USING ERRCODE = '23514';
                    END IF;
                END IF;

                IF NEW."CodigoEAN" IS NULL OR NEW."CodigoEAN" = '' OR
                   NEW."CodigoEAN" !~ '^[0-9]{13}$' THEN
                    RETURN NEW;
                END IF;

                -- No silent checksum normalization or reservation of invalid codes.
                IF ((10 - (
                      (SELECT sum(substr(NEW."CodigoEAN", n, 1)::integer *
                                   CASE WHEN n % 2 = 0 THEN 3 ELSE 1 END)
                       FROM generate_series(1, 12) AS n) % 10
                    )) % 10) <> substr(NEW."CodigoEAN", 13, 1)::integer THEN
                    RETURN NEW;
                END IF;

                INSERT INTO catalog.reservas_ean ("CodigoEAN", "ProductoId")
                VALUES (NEW."CodigoEAN", NEW."Id")
                ON CONFLICT ("CodigoEAN") DO NOTHING;

                SELECT r."ProductoId", r."Estado"
                  INTO existente, estado_actual
                FROM catalog.reservas_ean r
                WHERE r."CodigoEAN" = NEW."CodigoEAN";

                IF existente IS DISTINCT FROM NEW."Id" OR estado_actual <> 'Actual' THEN
                    RAISE EXCEPTION 'F451: EAN reservado historicamente; reutilizacion prohibida'
                        USING ERRCODE = '23505';
                END IF;
                RETURN NEW;
            END $fn$;

            CREATE TRIGGER "TR_f451_reservar_ean_producto"
            AFTER INSERT OR UPDATE OF "CodigoEAN" ON catalog.productos
            FOR EACH ROW EXECUTE FUNCTION catalog.f451_reservar_ean_producto();

            -- F454: each event uses the same SolicitudId with a new Version;
            -- a duplicate version is rejected atomically. Never rewrite event history.
            CREATE FUNCTION catalog.f451_auditoria_ean_inmutable() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                RAISE EXCEPTION 'F451: historial de rotaciones append-only'
                    USING ERRCODE = '23514';
            END $fn$;
            CREATE TRIGGER "TR_f451_rotaciones_append_only"
            BEFORE UPDATE OR DELETE ON catalog.rotaciones_ean
            FOR EACH ROW EXECUTE FUNCTION catalog.f451_auditoria_ean_inmutable();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $guard$
            BEGIN
                IF EXISTS (SELECT 1 FROM catalog.rotaciones_ean)
                   OR EXISTS (SELECT 1 FROM catalog.reservas_ean
                              WHERE "Origen" <> 'Backfill' OR "Estado" <> 'Actual')
                THEN
                    RAISE EXCEPTION 'F451: rollback bloqueado por EAN emitidos o historial; plan de recuperacion necesario';
                END IF;
            END $guard$;
            DROP TRIGGER "TR_f451_rotaciones_append_only" ON catalog.rotaciones_ean;
            DROP FUNCTION catalog.f451_auditoria_ean_inmutable();
            DROP TRIGGER "TR_f451_reservar_ean_producto" ON catalog.productos;
            DROP FUNCTION catalog.f451_reservar_ean_producto();
            DROP TABLE catalog.rotaciones_ean;
            DROP TABLE catalog.reservas_ean;
            """);
    }
}
