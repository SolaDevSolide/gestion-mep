using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <summary>
    /// Approbation et traçabilité (#94) : résolutions types figées dans la MEP, échéance signalée pendant la recette, compteurs
    /// d'incidents (échecs d'étape et de recette) et étape des événements d'exécution du journal. Les MEP existantes sont reprises
    /// une fois pour toutes depuis leur journal ; ensuite, plus rien ne se relit dans le texte du journal.
    /// </summary>
    public partial class ApprobationEtTracabilite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(name: "EcheanceSignaleeLe", table: "Meps", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<int>(name: "EchecsEtapes", table: "Meps", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "EchecsRecette", table: "Meps", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "ResolutionsFigees", table: "Meps", type: "jsonb", nullable: false, defaultValue: "[]");
            migrationBuilder.AddColumn<DateTimeOffset>(name: "ResolutionsFigeesLe", table: "Meps", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "EtapeId", table: "Journal", type: "uuid", nullable: true);

            // Étape des événements d'exécution existants : le code en tête du détail (« E0003 démarrée par… », « E0003 réussie : … »,
            // « E0003 en échec : … »), unique dans sa MEP.
            migrationBuilder.Sql("""
                UPDATE "Journal" AS j
                SET "EtapeId" = e."Id"
                FROM "Etapes" AS e
                WHERE e."MepId" = j."MepId"
                  AND j."Type" IN ('etape.demarree', 'etape.reussie', 'etape.echec')
                  AND split_part(j."Detail", ' ', 1) = e."Code";
                """);

            // Compteurs d'incidents : échecs d'étape (« etape.echec »), tests de recette en échec saisis (« … étape 2 en échec : … »)
            // et refus importés d'Excel (« …, 3 refusée(s) »).
            migrationBuilder.Sql("""
                UPDATE "Meps" AS m
                SET "EchecsEtapes" = (SELECT COUNT(*) FROM "Journal" AS j WHERE j."MepId" = m."Id" AND j."Type" = 'etape.echec'),
                    "EchecsRecette" =
                        (SELECT COUNT(*) FROM "Journal" AS j
                         WHERE j."MepId" = m."Id" AND j."Type" = 'recette.test' AND j."Detail" ~ ' étape [0-9]+ en échec : ')
                      + (SELECT COALESCE(SUM((regexp_match(j."Detail", '([0-9]+) refusée'))[1]::integer), 0) FROM "Journal" AS j
                         WHERE j."MepId" = m."Id" AND j."Type" = 'recette.import');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EcheanceSignaleeLe",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "EchecsEtapes",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "EchecsRecette",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "ResolutionsFigees",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "ResolutionsFigeesLe",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "EtapeId",
                table: "Journal");
        }
    }
}
