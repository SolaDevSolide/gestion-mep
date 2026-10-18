using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class EtapesAPlacer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bac « Étapes à placer » (#60) : les étapes existantes restent dans le graphe.
            migrationBuilder.AddColumn<bool>(
                name: "APlacer",
                table: "Etapes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "APlacer",
                table: "Etapes");
        }
    }
}
