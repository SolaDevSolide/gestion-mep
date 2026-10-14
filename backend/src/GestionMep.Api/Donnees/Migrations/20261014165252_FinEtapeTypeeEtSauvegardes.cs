using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class FinEtapeTypeeEtSauvegardes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Sauvegarde_Emplacement",
                table: "Systemes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Sauvegarde_MethodeRecuperation",
                table: "Systemes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Fin",
                table: "Etapes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResultatConstate",
                table: "Etapes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SauvegardeEmplacement",
                table: "Etapes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SauvegardeMethodeRecuperation",
                table: "Etapes",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sauvegarde_Emplacement",
                table: "Systemes");

            migrationBuilder.DropColumn(
                name: "Sauvegarde_MethodeRecuperation",
                table: "Systemes");

            migrationBuilder.DropColumn(
                name: "Fin",
                table: "Etapes");

            migrationBuilder.DropColumn(
                name: "ResultatConstate",
                table: "Etapes");

            migrationBuilder.DropColumn(
                name: "SauvegardeEmplacement",
                table: "Etapes");

            migrationBuilder.DropColumn(
                name: "SauvegardeMethodeRecuperation",
                table: "Etapes");
        }
    }
}
