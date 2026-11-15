-- F449: preflight READ-ONLY for historical EAN alias design (#70).
-- PostgreSQL 15+, catalog.productos as mapped in migration F419.
-- This script does NOT create tables, modify EANs or reserve aliases.
BEGIN TRANSACTION READ ONLY;

SELECT
    COUNT(*) AS productos_total,
    COUNT(*) FILTER (WHERE "CodigoEAN" IS NULL OR "CodigoEAN" = '') AS sin_ean,
    COUNT(*) FILTER (WHERE "CodigoEAN" IS NOT NULL AND "CodigoEAN" <> '') AS ean_no_vacios,
    COUNT(*) FILTER (
        WHERE "CodigoEAN" IS NOT NULL AND "CodigoEAN" <> ''
          AND "CodigoEAN" !~ '^[0-9]{13}$'
    ) AS ean_formato_no_conforme
FROM catalog.productos;

-- A nonempty barcode already used by multiple products must block alias backfill.
SELECT "CodigoEAN", COUNT(*) AS productos,
       array_agg("Id" ORDER BY "Id") AS producto_ids
FROM catalog.productos
WHERE "CodigoEAN" IS NOT NULL AND "CodigoEAN" <> ''
GROUP BY "CodigoEAN"
HAVING COUNT(*) > 1
ORDER BY productos DESC, "CodigoEAN";

-- EAN-13 checksum validation without normalizing historical strings.
WITH ean AS (
    SELECT "Id", "CodigoEAN"
    FROM catalog.productos
    WHERE "CodigoEAN" ~ '^[0-9]{13}$'
), sums AS (
    SELECT "Id", "CodigoEAN",
           (
               SELECT SUM((substr("CodigoEAN", n, 1)::integer) *
                          CASE WHEN n % 2 = 0 THEN 3 ELSE 1 END)
               FROM generate_series(1, 12) AS n
           ) AS weighted
    FROM ean
)
SELECT "Id", "CodigoEAN"
FROM sums
WHERE ((10 - (weighted % 10)) % 10) <> substr("CodigoEAN", 13, 1)::integer
ORDER BY "Id";

ROLLBACK;
