using MarketplaceERP.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Core.Migrations;

/// <summary>
/// F369: privacy-preserving, shared per-identifier login rate limiting.
/// Adds only a new table and index. OFF until verified and enabled explicitly.
/// </summary>
[DbContext(typeof(CoreDbContext))]
[Migration("20261115042500_AgregarThrottleLoginCuenta")]
public sealed class AgregarThrottleLoginCuenta : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "login_throttle",
            schema: "core",
            columns: table => new
            {
                AccountKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                WindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Requests = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_login_throttle", x => x.AccountKey));

        migrationBuilder.CreateIndex(
            name: "IX_login_throttle_WindowStartUtc",
            schema: "core",
            table: "login_throttle",
            column: "WindowStartUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "login_throttle", schema: "core");
    }
}
