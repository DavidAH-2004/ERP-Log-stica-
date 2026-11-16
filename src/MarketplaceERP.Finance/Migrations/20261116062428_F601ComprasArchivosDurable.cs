using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketplaceERP.Finance.Migrations
{
    /// <inheritdoc />
    public partial class F601ComprasArchivosDurable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archivos_compras",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenCompraId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroPO = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProveedorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProveedorNombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProveedorNit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Contacto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Correo = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Telefono = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FechaCreacionUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaActualizacionUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CantidadItems = table.Column<int>(type: "integer", nullable: false),
                    MontoTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PdfContenido = table.Column<byte[]>(type: "bytea", nullable: false),
                    PdfSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VersionPdf = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archivos_compras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_archivos_compras_ordenes_compra_OrdenCompraId",
                        column: x => x.OrdenCompraId,
                        principalSchema: "finance",
                        principalTable: "ordenes_compra",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_archivos_compras_FechaCreacionUtc",
                schema: "finance",
                table: "archivos_compras",
                column: "FechaCreacionUtc");

            migrationBuilder.CreateIndex(
                name: "IX_archivos_compras_OrdenCompraId",
                schema: "finance",
                table: "archivos_compras",
                column: "OrdenCompraId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_archivos_compras_ProveedorId",
                schema: "finance",
                table: "archivos_compras",
                column: "ProveedorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping this table silently destroys issued purchase-order PDFs.
            // Recovery requires an independently verified PostgreSQL backup.
            throw new InvalidOperationException(
                "Compras Archivos: Down destructivo bloqueado; restaurar desde un respaldo validado.");
        }
    }
}
