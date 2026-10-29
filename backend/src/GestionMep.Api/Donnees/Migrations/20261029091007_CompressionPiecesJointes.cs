using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class CompressionPiecesJointes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Compression",
                table: "PiecesJointes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TailleStockee",
                table: "PiecesJointes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Pièces déjà déposées : stockées telles quelles, jamais examinées (Compression null) ; la commande
            // « gestion-mep pieces-jointes compresser » les compresse ensuite.
            migrationBuilder.Sql("""UPDATE "PiecesJointes" SET "TailleStockee" = "Taille";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Compression",
                table: "PiecesJointes");

            migrationBuilder.DropColumn(
                name: "TailleStockee",
                table: "PiecesJointes");
        }
    }
}
