using MarketplaceERP.Catalog.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations;

/// <summary>
/// F470: internal-prefix EAN rotation by two distinct administrators.
/// Database functions are inert until an explicit Admin API feature switch is enabled.
/// Safe, additive, never renumbers historic codes during Up().
/// </summary>
[DbContext(typeof(CatalogDbContext))]
[Migration("20261116000500_F470RotacionEanDobleAprobacion")]
public partial class F470RotacionEanDobleAprobacion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- Only internal company codes (prefix 20) can be newly issued.
            -- A valid checksum does not authorize an external GS1 code.
            CREATE FUNCTION catalog.f470_es_ean13_interno(p_codigo text)
            RETURNS boolean LANGUAGE sql IMMUTABLE STRICT AS $f$
                SELECT p_codigo ~ '^20[0-9]{11}$'
                   AND ((10 - (
                     (SELECT sum(substr(p_codigo, n, 1)::int *
                                 CASE WHEN n % 2 = 0 THEN 3 ELSE 1 END)
                      FROM generate_series(1, 12) AS n) % 10
                    )) % 10) = substr(p_codigo, 13, 1)::int;
            $f$;

            -- Step 1: submit a durable request, without changing product or
            -- reserving a code. A second Admin must independently approve it.
            CREATE FUNCTION catalog.f470_solicitar_rotacion(
                p_solicitud uuid, p_producto uuid, p_nuevo text,
                p_motivo text, p_solicitado_por uuid)
            RETURNS text LANGUAGE plpgsql AS $f$
            DECLARE
                actual text;
                previo catalog.rotaciones_ean%ROWTYPE;
            BEGIN
                IF p_solicitud IS NULL OR p_solicitud = '00000000-0000-0000-0000-000000000000'::uuid
                   OR p_producto IS NULL OR p_producto = '00000000-0000-0000-0000-000000000000'::uuid
                   OR p_solicitado_por IS NULL OR p_solicitado_por = '00000000-0000-0000-0000-000000000000'::uuid
                   OR p_motivo IS NULL OR length(trim(p_motivo)) NOT BETWEEN 10 AND 1000
                   OR NOT coalesce(catalog.f470_es_ean13_interno(p_nuevo), false)
                THEN
                    RAISE EXCEPTION 'F470: solicitud invalida, se requiere EAN interno y justificacion'
                        USING ERRCODE = '23514';
                END IF;

                PERFORM pg_advisory_xact_lock(hashtextextended(p_solicitud::text, 470));
                SELECT * INTO previo FROM catalog.rotaciones_ean
                    WHERE "SolicitudId" = p_solicitud
                    ORDER BY "Version" DESC LIMIT 1;

                IF FOUND THEN
                    IF previo."ProductoId" <> p_producto
                       OR previo."CodigoNuevo" <> p_nuevo
                       OR previo."Motivo" <> trim(p_motivo)
                       OR previo."SolicitadoPor" <> p_solicitado_por
                    THEN
                        RAISE EXCEPTION 'F470: idempotency key reused with different parameters'
                            USING ERRCODE = '23505';
                    END IF;
                    RETURN previo."Estado";
                END IF;

                SELECT "CodigoEAN" INTO actual FROM catalog.productos
                    WHERE "Id" = p_producto FOR UPDATE;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'F470: producto inexistente' USING ERRCODE = 'P0002';
                END IF;
                IF actual IS NULL OR actual !~ '^[0-9]{13}$'
                   OR actual = p_nuevo
                   OR NOT EXISTS (
                       SELECT 1 FROM catalog.reservas_ean r
                       WHERE r."CodigoEAN" = actual
                         AND r."ProductoId" = p_producto
                         AND r."Estado" = 'Actual')
                THEN
                    RAISE EXCEPTION 'F470: el EAN actual no tiene reserva vigente'
                        USING ERRCODE = '23514';
                END IF;
                IF EXISTS (SELECT 1 FROM catalog.reservas_ean r WHERE r."CodigoEAN" = p_nuevo)
                   OR EXISTS (SELECT 1 FROM catalog.productos p WHERE p."CodigoEAN" = p_nuevo)
                THEN
                    RAISE EXCEPTION 'F470: codigo nuevo ya usado o reservado'
                        USING ERRCODE = '23505';
                END IF;

                INSERT INTO catalog.rotaciones_ean
                    ("Id","SolicitudId","ProductoId","CodigoAnterior","CodigoNuevo",
                     "Motivo","SolicitadoPor","Version","Estado")
                VALUES (gen_random_uuid(),p_solicitud,p_producto,actual,p_nuevo,
                        trim(p_motivo),p_solicitado_por,1,'Solicitada');
                RETURN 'Solicitada';
            END $f$;

            -- Step 2: final approval executes one atomic transition.
            -- PostgreSQL serializes the request AND locks the product, so
            -- concurrent requests cannot redirect the old EAN to a new SKU.
            CREATE FUNCTION catalog.f470_aprobar_rotacion(
                p_solicitud uuid, p_aprobado_por uuid)
            RETURNS text LANGUAGE plpgsql AS $f$
            DECLARE
                previa catalog.rotaciones_ean%ROWTYPE;
                actual text;
            BEGIN
                IF p_solicitud IS NULL OR p_aprobado_por IS NULL
                   OR p_aprobado_por = '00000000-0000-0000-0000-000000000000'::uuid
                THEN
                    RAISE EXCEPTION 'F470: aprobacion invalida' USING ERRCODE = '23514';
                END IF;

                PERFORM pg_advisory_xact_lock(hashtextextended(p_solicitud::text, 470));
                SELECT * INTO previa FROM catalog.rotaciones_ean
                    WHERE "SolicitudId" = p_solicitud
                    ORDER BY "Version" DESC LIMIT 1;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'F470: solicitud inexistente' USING ERRCODE = 'P0002';
                END IF;
                IF previa."Estado" = 'Ejecutada' THEN
                    RETURN 'Ejecutada'; -- durable idempotency: never rotate twice
                END IF;
                IF previa."Estado" <> 'Solicitada' OR previa."Version" <> 1 THEN
                    RAISE EXCEPTION 'F470: transicion de estado invalida'
                        USING ERRCODE = '23514';
                END IF;
                IF previa."SolicitadoPor" = p_aprobado_por THEN
                    RAISE EXCEPTION 'F470: prohibida autoaprobacion'
                        USING ERRCODE = '23514';
                END IF;

                SELECT "CodigoEAN" INTO actual FROM catalog.productos
                    WHERE "Id" = previa."ProductoId" FOR UPDATE;
                IF NOT FOUND OR actual IS DISTINCT FROM previa."CodigoAnterior"
                   OR NOT coalesce(catalog.f470_es_ean13_interno(previa."CodigoNuevo"), false)
                THEN
                    RAISE EXCEPTION 'F470: EAN vigente cambio o solicitud obsoleta'
                        USING ERRCODE = '23514';
                END IF;

                IF EXISTS (SELECT 1 FROM catalog.reservas_ean
                           WHERE "CodigoEAN" = previa."CodigoNuevo")
                   OR EXISTS (SELECT 1 FROM catalog.productos
                              WHERE "CodigoEAN" = previa."CodigoNuevo")
                THEN
                    RAISE EXCEPTION 'F470: codigo reservado por otro producto o intento'
                        USING ERRCODE = '23505';
                END IF;

                -- Approval event commits only if every subsequent step succeeds.
                INSERT INTO catalog.rotaciones_ean
                    ("Id","SolicitudId","ProductoId","CodigoAnterior","CodigoNuevo",
                     "Motivo","SolicitadoPor","AprobadoPor",
                     "AprobadoUtc","Version","Estado")
                VALUES (gen_random_uuid(),previa."SolicitudId",previa."ProductoId",
                        previa."CodigoAnterior",previa."CodigoNuevo",previa."Motivo",
                        previa."SolicitadoPor",p_aprobado_por,now(),2,'Aprobada');

                UPDATE catalog.reservas_ean
                   SET "Estado" = 'Alias', "UltimaTransicionUtc" = now()
                 WHERE "CodigoEAN" = previa."CodigoAnterior"
                   AND "ProductoId" = previa."ProductoId" AND "Estado" = 'Actual';
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'F470: reserva anterior perdida'
                        USING ERRCODE = '23514';
                END IF;

                INSERT INTO catalog.reservas_ean
                    ("CodigoEAN","ProductoId","Estado","Origen")
                VALUES (previa."CodigoNuevo",previa."ProductoId",'Actual','Emision');

                UPDATE catalog.productos
                   SET "CodigoEAN" = previa."CodigoNuevo"
                 WHERE "Id" = previa."ProductoId"
                   AND "CodigoEAN" = previa."CodigoAnterior";
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'F470: producto cambio concurrentemente'
                        USING ERRCODE = '23514';
                END IF;

                INSERT INTO catalog.rotaciones_ean
                    ("Id","SolicitudId","ProductoId","CodigoAnterior","CodigoNuevo",
                     "Motivo","SolicitadoPor","AprobadoPor","AprobadoUtc",
                     "EjecutadoUtc","Version","Estado")
                VALUES (gen_random_uuid(),previa."SolicitudId",previa."ProductoId",
                        previa."CodigoAnterior",previa."CodigoNuevo",previa."Motivo",
                        previa."SolicitadoPor",p_aprobado_por,now(),now(),3,'Ejecutada');
                RETURN 'Ejecutada';
            END $f$;

            -- F481: requester-only cancellation BEFORE approval. The
            -- event is immutable (Version 2, Rechazada), no EAN or stock changes.
            -- Same request-scoped advisory lock as approval across all nodes.
            CREATE FUNCTION catalog.f481_cancelar_rotacion(
                p_solicitud uuid, p_actor uuid)
            RETURNS text LANGUAGE plpgsql AS $f$
            DECLARE
                previa catalog.rotaciones_ean%ROWTYPE;
            BEGIN
                IF p_solicitud IS NULL OR p_actor IS NULL
                   OR p_solicitud = '00000000-0000-0000-0000-000000000000'::uuid
                   OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
                THEN
                    RAISE EXCEPTION 'F481: solicitud o actor invalido'
                        USING ERRCODE = '23514';
                END IF;

                PERFORM pg_advisory_xact_lock(hashtextextended(p_solicitud::text, 470));
                SELECT * INTO previa FROM catalog.rotaciones_ean
                    WHERE "SolicitudId" = p_solicitud
                    ORDER BY "Version" DESC LIMIT 1;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'F481: solicitud inexistente'
                        USING ERRCODE = 'P0002';
                END IF;
                IF previa."SolicitadoPor" IS DISTINCT FROM p_actor THEN
                    RAISE EXCEPTION 'F481: solo el solicitante puede cancelar'
                        USING ERRCODE = '23514';
                END IF;
                IF previa."Estado" = 'Rechazada' THEN
                    RETURN 'Rechazada'; -- replay must not append another event
                END IF;
                IF previa."Estado" <> 'Solicitada' OR previa."Version" <> 1 THEN
                    RAISE EXCEPTION 'F481: solicitud ya aprobada o ejecutada'
                        USING ERRCODE = '23514';
                END IF;

                INSERT INTO catalog.rotaciones_ean
                    ("Id","SolicitudId","ProductoId","CodigoAnterior",
                     "CodigoNuevo","Motivo","SolicitadoPor","Version","Estado")
                VALUES (gen_random_uuid(),previa."SolicitudId",previa."ProductoId",
                        previa."CodigoAnterior",previa."CodigoNuevo",previa."Motivo",
                        previa."SolicitadoPor",2,'Rechazada');
                RETURN 'Rechazada';
            END $f$;
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
                    -- A downgrade must not drop F470 SQL functions when the
                    -- F451 schema has new reservations or immutable aliases.
                    RAISE EXCEPTION 'F470: rollback blocked by EAN issuance, aliases or rotation history';
                END IF;
            END $guard$;
            DROP FUNCTION catalog.f481_cancelar_rotacion(uuid,uuid);
            DROP FUNCTION catalog.f470_aprobar_rotacion(uuid,uuid);
            DROP FUNCTION catalog.f470_solicitar_rotacion(uuid,uuid,text,text,uuid);
            DROP FUNCTION catalog.f470_es_ean13_interno(text);
            """);
    }
}
