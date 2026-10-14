using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class CommunicationEtModelesTexte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Communication_ActionUtilisateurs",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Communication_ArretApplicatif",
                table: "Meps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Communication_Benefices",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "Communication_DateAnnoncee",
                table: "Meps",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "Communication_HeureDebutAnnoncee",
                table: "Meps",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "Communication_HeureFinAnnoncee",
                table: "Meps",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "Communication_HeureLimite",
                table: "Meps",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Communication_NiveauRisque",
                table: "Meps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "Communication_PreuveValidationId",
                table: "Meps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Communication_ReferenceDemande",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Communication_UtilisateursImpactes",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "Communication_ValideeLe",
                table: "Meps",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Communication_ValideePar",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Communication_ValideeParUtilisateurs",
                table: "Meps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ContactSupport",
                table: "Equipes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Signature",
                table: "Equipes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ModelesTexte",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Usage = table.Column<int>(type: "integer", nullable: false),
                    Objet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Corps = table.Column<string>(type: "text", nullable: false),
                    CleFournie = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifieLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifieParId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelesTexte", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelesTexte_Equipes_EquipeId",
                        column: x => x.EquipeId,
                        principalTable: "Equipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelesTexte_EquipeId_CleFournie",
                table: "ModelesTexte",
                columns: new[] { "EquipeId", "CleFournie" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelesTexte");

            migrationBuilder.DropColumn(
                name: "Communication_ActionUtilisateurs",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_ArretApplicatif",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_Benefices",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_DateAnnoncee",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_HeureDebutAnnoncee",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_HeureFinAnnoncee",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_HeureLimite",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_NiveauRisque",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_PreuveValidationId",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_ReferenceDemande",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_UtilisateursImpactes",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_ValideeLe",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_ValideePar",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Communication_ValideeParUtilisateurs",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "ContactSupport",
                table: "Equipes");

            migrationBuilder.DropColumn(
                name: "Signature",
                table: "Equipes");
        }
    }
}
