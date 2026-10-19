using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <summary>
    /// Cycle de vie (#93) : annulation, report, clôture en échec (état du service, référence d'incident), pilotage de secours,
    /// premier Go conservé et étapes correctives ajoutées après le Go.
    /// </summary>
    public partial class AnnulationReportEchecEtPilotage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Annulation",
                table: "Meps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EtatService",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PilotageSecours",
                table: "Meps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PremierGoLe",
                table: "Meps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceIncident",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Report",
                table: "Meps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AjouteeApresGo",
                table: "Etapes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Premier Go des MEP existantes : le plus ancien Go du journal, à défaut le dernier Go connu (un retour en
            // préparation suivi d'un nouveau Go l'avait remplacé).
            migrationBuilder.Sql("""
                UPDATE "Meps" AS m
                SET "PremierGoLe" = COALESCE(
                    (SELECT MIN(j."Quand") FROM "Journal" AS j WHERE j."MepId" = m."Id" AND j."Type" = 'gonogo.go'),
                    m."DemarrageAutoriseLe")
                WHERE m."DemarrageAutoriseLe" IS NOT NULL
                   OR EXISTS (SELECT 1 FROM "Journal" AS j WHERE j."MepId" = m."Id" AND j."Type" = 'gonogo.go');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Annulation",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "EtatService",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "PilotageSecours",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "PremierGoLe",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "ReferenceIncident",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Report",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "AjouteeApresGo",
                table: "Etapes");
        }
    }
}
