using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class RevisionsDesBlocs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RevisionSauvegarde",
                table: "Systemes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Revisions_Approbateurs",
                table: "Meps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Revisions_Communication",
                table: "Meps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Revisions_Echeance",
                table: "Meps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Revisions_Identite",
                table: "Meps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Revisions_PlanRepli",
                table: "Meps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Revisions_ResponsablesTechniques",
                table: "Meps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Revisions_Sauvegarde",
                table: "Meps",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevisionSauvegarde",
                table: "Systemes");

            migrationBuilder.DropColumn(
                name: "Revisions_Approbateurs",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Revisions_Communication",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Revisions_Echeance",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Revisions_Identite",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Revisions_PlanRepli",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Revisions_ResponsablesTechniques",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Revisions_Sauvegarde",
                table: "Meps");
        }
    }
}
