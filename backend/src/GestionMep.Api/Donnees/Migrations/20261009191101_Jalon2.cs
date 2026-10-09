using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class Jalon2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CatalogueId",
                table: "Systemes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClotureeLe",
                table: "Meps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClotureeParId",
                table: "Meps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Issue",
                table: "Meps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanRepli_DonneesDepuisSauvegarde",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PlanRepli_DureeTesteeMinutes",
                table: "Meps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanRepli_QuandDeclencher",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PlanRepli_SiRepliEchoue",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PlanRepli_VersionARestaurer",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetourArriereDeclencheLe",
                table: "Meps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RetourArriereDeclenchePar",
                table: "Meps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetourArriereMotif",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutantId",
                table: "Etapes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Plan",
                table: "Etapes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "ResolutionIds",
                table: "Etapes",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::uuid[]");

            migrationBuilder.CreateTable(
                name: "ExecutionsResolution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MepId = table.Column<Guid>(type: "uuid", nullable: false),
                    EtapeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolutionTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titre = table.Column<string>(type: "text", nullable: false),
                    Symptomes = table.Column<string>(type: "text", nullable: false),
                    Diagnostic = table.Column<string>(type: "text", nullable: false),
                    Corrections = table.Column<string>(type: "jsonb", nullable: false),
                    VerificationInstruction = table.Column<string>(type: "text", nullable: false),
                    VerificationAttendu = table.Column<string>(type: "text", nullable: false),
                    VerificationResultat = table.Column<int>(type: "integer", nullable: true),
                    VerificationObservation = table.Column<string>(type: "text", nullable: false),
                    Statut = table.Column<int>(type: "integer", nullable: false),
                    DemarreeParId = table.Column<Guid>(type: "uuid", nullable: true),
                    DemarreeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TermineeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionsResolution", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionsResolution_Meps_MepId",
                        column: x => x.MepId,
                        principalTable: "Meps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResolutionsTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Titre = table.Column<string>(type: "text", nullable: false),
                    Symptomes = table.Column<string>(type: "text", nullable: false),
                    Diagnostic = table.Column<string>(type: "text", nullable: false),
                    SystemeCatalogueId = table.Column<Guid>(type: "uuid", nullable: true),
                    Corrections = table.Column<string>(type: "jsonb", nullable: false),
                    VerificationInstruction = table.Column<string>(type: "text", nullable: false),
                    VerificationAttendu = table.Column<string>(type: "text", nullable: false),
                    Actif = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ModifieeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResolutionsTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemesCatalogue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Cible = table.Column<string>(type: "text", nullable: false),
                    Actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemesCatalogue", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionsResolution_EtapeId",
                table: "ExecutionsResolution",
                column: "EtapeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionsResolution_MepId",
                table: "ExecutionsResolution",
                column: "MepId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemesCatalogue_Nom",
                table: "SystemesCatalogue",
                column: "Nom",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionsResolution");

            migrationBuilder.DropTable(
                name: "ResolutionsTypes");

            migrationBuilder.DropTable(
                name: "SystemesCatalogue");

            migrationBuilder.DropColumn(
                name: "CatalogueId",
                table: "Systemes");

            migrationBuilder.DropColumn(
                name: "ClotureeLe",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "ClotureeParId",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Issue",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "PlanRepli_DonneesDepuisSauvegarde",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "PlanRepli_DureeTesteeMinutes",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "PlanRepli_QuandDeclencher",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "PlanRepli_SiRepliEchoue",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "PlanRepli_VersionARestaurer",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "RetourArriereDeclencheLe",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "RetourArriereDeclenchePar",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "RetourArriereMotif",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "ExecutantId",
                table: "Etapes");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Etapes");

            migrationBuilder.DropColumn(
                name: "ResolutionIds",
                table: "Etapes");
        }
    }
}
