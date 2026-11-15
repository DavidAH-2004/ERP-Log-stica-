using MarketplaceERP.Core.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Core.Migrations;

/// <summary>
/// F360: session generation shared in PostgreSQL. This schema change is
/// backward-compatible; enforcement remains separately gated off by default.
/// </summary>
[DbContext(typeof(CoreDbContext))]
[Migration("20261115032000_AgregarVersionSesionJWT")]
public sealed class AgregarVersionSesionJWT : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "VersionSesion",
            schema: "core",
            table: "usuarios",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "VersionSesion",
            schema: "core",
            table: "usuarios");
    }
}
