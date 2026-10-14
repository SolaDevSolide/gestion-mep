using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class BibliothequeEtapesModeles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreeeLe",
                table: "ResolutionsTypes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "Utilisations",
                table: "ResolutionsTypes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreeLe",
                table: "ModelesRecette",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "Utilisations",
                table: "ModelesRecette",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "ElementsBibliothequeComptes",
                table: "Meps",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<Guid>(
                name: "ModeleEtapeId",
                table: "Etapes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ModelesEtape",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    SystemeCatalogueIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Criticite = table.Column<int>(type: "integer", nullable: true),
                    DureeEstimeeMinutes = table.Column<int>(type: "integer", nullable: true),
                    Instructions = table.Column<string>(type: "text", nullable: false),
                    ResultatAttendu = table.Column<string>(type: "text", nullable: false),
                    Fin = table.Column<int>(type: "integer", nullable: false),
                    SauvegardeEmplacement = table.Column<string>(type: "text", nullable: false),
                    SauvegardeMethodeRecuperation = table.Column<string>(type: "text", nullable: false),
                    ResolutionIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    Utilisations = table.Column<int>(type: "integer", nullable: false),
                    CreeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ModifieLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifieParId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelesEtape", x => x.Id);
                });

            // Dates de création : première version archivée d'une résolution type, dernière modification d'un modèle de recette.
            migrationBuilder.Sql("""
                UPDATE "ResolutionsTypes" r SET "CreeeLe" = COALESCE(
                    (SELECT min(v."ModifieeLe") FROM "VersionsResolutionType" v WHERE v."ResolutionTypeId" = r."Id"), r."ModifieeLe");
                UPDATE "ModelesRecette" SET "CreeLe" = "ModifieLe";
                """);
            // Utilisations des résolutions types : MEP (hors modèles) dont une étape est liée à la résolution, notées comme comptées.
            migrationBuilder.Sql("""
                UPDATE "Meps" m SET "ElementsBibliothequeComptes" = COALESCE(
                    (SELECT array_agg(DISTINCT r ORDER BY r) FROM "Etapes" e, unnest(e."ResolutionIds") AS r WHERE e."MepId" = m."Id"), '{}'::uuid[])
                WHERE NOT m."EstModele";
                UPDATE "ResolutionsTypes" r SET "Utilisations" = (SELECT count(*) FROM "Meps" m WHERE r."Id" = ANY(m."ElementsBibliothequeComptes"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelesEtape");

            migrationBuilder.DropColumn(
                name: "CreeeLe",
                table: "ResolutionsTypes");

            migrationBuilder.DropColumn(
                name: "Utilisations",
                table: "ResolutionsTypes");

            migrationBuilder.DropColumn(
                name: "CreeLe",
                table: "ModelesRecette");

            migrationBuilder.DropColumn(
                name: "Utilisations",
                table: "ModelesRecette");

            migrationBuilder.DropColumn(
                name: "ElementsBibliothequeComptes",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "ModeleEtapeId",
                table: "Etapes");
        }
    }
}
