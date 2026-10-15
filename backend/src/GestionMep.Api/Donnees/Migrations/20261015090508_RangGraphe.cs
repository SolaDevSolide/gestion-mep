using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class RangGraphe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ordre gauche-droite choisi dans le graphe (#76) : 0 pour les étapes existantes, leur ordre ne change pas.
            migrationBuilder.AddColumn<int>(
                name: "Rang",
                table: "Etapes",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rang",
                table: "Etapes");
        }
    }
}
