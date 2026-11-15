using MarketplaceERP.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Core.Migrations;

/// <summary>
/// F369: optional distributed per-account login throttle using PostgreSQL.
/// Additive migration; enforcement is OFF unless explicitly enabled after QA.
/// </summary>
[DbContext(typeof(CoreDbContext))]
[Migration("20261115042500_AgregarThrottleLoginCuenta")]
public sealed class AgregarThrottleLoginCuenta : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "IntentosLoginVentana",
            schema: "core",
            table: "usuarios",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "InicioVentanaLoginUtc",
            schema: "core",
            table: "usuarios",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IntentosLoginVentana", schema: "core", table: "usuarios");
        migrationBuilder.DropColumn(name: "InicioVentanaLoginUtc", schema: "core", table: "usuarios");
    }
}
