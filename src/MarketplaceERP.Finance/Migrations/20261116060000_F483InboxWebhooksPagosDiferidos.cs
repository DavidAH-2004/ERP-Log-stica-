using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations;

/// <summary>
/// F483: isolated, append-only signed-event inbox. An event is NEVER a
/// charge confirmation. No real provider is registered, and no endpoint
/// accepts this record in the API. Test on temporary localhost PG only.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20261116060000_F483InboxWebhooksPagosDiferidos")]
public partial class F483InboxWebhooksPagosDiferidos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE finance.webhooks_pago_inbox (
                "Proveedor" varchar(24) NOT NULL,
                "CuentaId" uuid NOT NULL,
                "AplicativoId" uuid NOT NULL,
                "TransaccionId" uuid NOT NULL,
                "HuellaCuerpo" char(64) NOT NULL,
                "Estado" varchar(32) NOT NULL DEFAULT 'RequiereConsulta',
                "RecibidoUtc" timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "PK_webhooks_pago_inbox"
                    PRIMARY KEY ("Proveedor", "CuentaId", "AplicativoId", "TransaccionId"),
                CONSTRAINT "CK_webhooks_pago_inbox_huella"
                    CHECK ("HuellaCuerpo" ~ '^[A-F0-9]{64}$'),
                CONSTRAINT "CK_webhooks_pago_inbox_pendiente"
                    CHECK ("Estado" = 'RequiereConsulta')
            );

            -- A signed notification is evidence to independently query
            -- the provider, NEVER a reason to mutate commercial accounting.
            CREATE FUNCTION finance.f483_inbox_inmutable() RETURNS trigger
            LANGUAGE plpgsql AS $f$
            BEGIN
                RAISE EXCEPTION 'F483: inbox append-only; use separate reconciliation ledger'
                    USING ERRCODE = '23514';
            END $f$;
            CREATE TRIGGER "TR_f483_inbox_append_only"
              BEFORE UPDATE OR DELETE ON finance.webhooks_pago_inbox
              FOR EACH ROW EXECUTE FUNCTION finance.f483_inbox_inmutable();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $guard$
            BEGIN
                IF EXISTS (SELECT 1 FROM finance.webhooks_pago_inbox) THEN
                    RAISE EXCEPTION 'F483: rollback blocked while inbox records exist';
                END IF;
            END $guard$;
            DROP TRIGGER "TR_f483_inbox_append_only" ON finance.webhooks_pago_inbox;
            DROP FUNCTION finance.f483_inbox_inmutable();
            DROP TABLE finance.webhooks_pago_inbox;
            """);
    }
}
