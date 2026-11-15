using System;
using MarketplaceERP.Catalog.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Catalog.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20261115075500_SesionesConteoEscaner")]
    public partial class SesionesConteoEscaner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Serial único en PostgreSQL: dos equipos no pueden obtener el mismo número.
            migrationBuilder.Sql("""
                CREATE SEQUENCE catalog.sesiones_conteo_consecutivo
                    AS bigint INCREMENT BY 1 MINVALUE 1 MAXVALUE 999999999
                    START WITH 1 NO CYCLE;
                """);
            migrationBuilder.AddColumn<Guid>(
                name: "SesionConteoId",
                schema: "catalog",
                table: "conteos_ciclicos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sesiones_conteo",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Modo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Pasillo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreadaPor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaCierre = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Observaciones = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sesiones_conteo", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "hallazgos_conteo",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SesionConteoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NombreProducto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Pasillo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false),
                    Usuario = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hallazgos_conteo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_hallazgos_conteo_sesiones_conteo_SesionConteoId",
                        column: x => x.SesionConteoId,
                        principalSchema: "catalog",
                        principalTable: "sesiones_conteo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lecturas_conteo",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SesionConteoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodigoLeido = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    ProductoId = table.Column<Guid>(type: "uuid", nullable: true),
                    StockFisicoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConteoCiclicoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Cantidad = table.Column<int>(type: "integer", nullable: true),
                    Resultado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Usuario = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lecturas_conteo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lecturas_conteo_sesiones_conteo_SesionConteoId",
                        column: x => x.SesionConteoId,
                        principalSchema: "catalog",
                        principalTable: "sesiones_conteo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conteos_ciclicos_SesionConteoId",
                schema: "catalog",
                table: "conteos_ciclicos",
                column: "SesionConteoId");

            migrationBuilder.CreateIndex(
                name: "IX_hallazgos_conteo_SesionConteoId",
                schema: "catalog",
                table: "hallazgos_conteo",
                column: "SesionConteoId");

            migrationBuilder.CreateIndex(
                name: "IX_lecturas_conteo_SesionConteoId",
                schema: "catalog",
                table: "lecturas_conteo",
                column: "SesionConteoId");

            migrationBuilder.CreateIndex(
                name: "IX_sesiones_conteo_Numero",
                schema: "catalog",
                table: "sesiones_conteo",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sesiones_conteo_Pasillo",
                schema: "catalog",
                table: "sesiones_conteo",
                column: "Pasillo",
                unique: true,
                filter: "\"Modo\" = 'Recorrido' AND \"Estado\" = 'Abierta'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No borrar auditoría ni lecturas: el rollback debe fallar cerrado.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM catalog.sesiones_conteo LIMIT 1)
                  OR EXISTS (SELECT 1 FROM catalog.conteos_ciclicos WHERE "SesionConteoId" IS NOT NULL LIMIT 1)
                  THEN
                    RAISE EXCEPTION 'Rollback bloqueado: existen sesiones de conteo o referencias que deben preservarse.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "hallazgos_conteo",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "lecturas_conteo",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "sesiones_conteo",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "IX_conteos_ciclicos_SesionConteoId",
                schema: "catalog",
                table: "conteos_ciclicos");

            migrationBuilder.DropColumn(
                name: "SesionConteoId",
                schema: "catalog",
                table: "conteos_ciclicos");
            migrationBuilder.Sql("DROP SEQUENCE catalog.sesiones_conteo_consecutivo;");
        }
    }
}
