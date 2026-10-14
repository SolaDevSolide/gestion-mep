using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class RetourArriereFacultatif : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RetourArriereActive",
                table: "Meps",
                type: "boolean",
                nullable: false,
                defaultValue: true); // retour arrière activé par défaut, y compris pour les MEP existantes
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetourArriereActive",
                table: "Meps");
        }
    }
}
