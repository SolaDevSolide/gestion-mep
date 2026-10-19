using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <summary>
    /// Comptes et API (#98) : personne ajoutée d'office à une MEP (administrateur qui démarre une étape ou prend la main), qui
    /// n'est qu'exécutante, jamais suppléante implicite d'une MEP simplifiée. Les personnes existantes ne sont pas marquées.
    /// </summary>
    public partial class ParticipantsAjoutesDOffice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AjouteDOffice",
                table: "Participants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AjouteDOffice",
                table: "Participants");
        }
    }
}
