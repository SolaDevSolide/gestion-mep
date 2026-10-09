using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class Jalon3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Approbations",
                table: "Meps",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "Bilan",
                table: "Meps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BilanActive",
                table: "Meps",
                type: "boolean",
                nullable: false,
                defaultValue: true); // bilan activé par défaut, y compris pour les MEP existantes

            migrationBuilder.AddColumn<string>(
                name: "Environnement",
                table: "Meps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "EstModele",
                table: "Meps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GoNoGo",
                table: "Meps",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "Sauvegarde_Mode",
                table: "Meps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "SuppleantIds",
                table: "Meps",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            // Reprise de l'ancien suppléant unique avant de supprimer sa colonne.
            migrationBuilder.Sql("""UPDATE "Meps" SET "SuppleantIds" = ARRAY["SuppleantId"] WHERE "SuppleantId" IS NOT NULL;""");

            migrationBuilder.DropColumn(
                name: "SuppleantId",
                table: "Meps");

            migrationBuilder.AddColumn<string>(
                name: "TestsRecette",
                table: "Meps",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NonRetourAutoriseLe",
                table: "Etapes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NonRetourAutoriseParId",
                table: "Etapes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PointDeNonRetour",
                table: "Etapes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Etapes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CriteresGoNoGo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Libelle = table.Column<string>(type: "text", nullable: false),
                    Ordre = table.Column<int>(type: "integer", nullable: false),
                    Actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriteresGoNoGo", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Parametres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ApprobationsMinimum = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parametres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PiecesJointes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MepId = table.Column<Guid>(type: "uuid", nullable: true),
                    EtapeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExecutionResolutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    NomFichier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TypeContenu = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Taille = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeposeParId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeposeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PiecesJointes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PiecesJointes_Etapes_EtapeId",
                        column: x => x.EtapeId,
                        principalTable: "Etapes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PiecesJointes_ExecutionsResolution_ExecutionResolutionId",
                        column: x => x.ExecutionResolutionId,
                        principalTable: "ExecutionsResolution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PiecesJointes_Meps_MepId",
                        column: x => x.MepId,
                        principalTable: "Meps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PiecesJointes_ResolutionsTypes_ResolutionTypeId",
                        column: x => x.ResolutionTypeId,
                        principalTable: "ResolutionsTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Parametres",
                columns: new[] { "Id", "ApprobationsMinimum" },
                values: new object[] { 1, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_Meps_EstModele",
                table: "Meps",
                column: "EstModele");

            migrationBuilder.CreateIndex(
                name: "IX_PiecesJointes_EtapeId",
                table: "PiecesJointes",
                column: "EtapeId");

            migrationBuilder.CreateIndex(
                name: "IX_PiecesJointes_ExecutionResolutionId",
                table: "PiecesJointes",
                column: "ExecutionResolutionId");

            migrationBuilder.CreateIndex(
                name: "IX_PiecesJointes_MepId",
                table: "PiecesJointes",
                column: "MepId");

            migrationBuilder.CreateIndex(
                name: "IX_PiecesJointes_ResolutionTypeId",
                table: "PiecesJointes",
                column: "ResolutionTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CriteresGoNoGo");

            migrationBuilder.DropTable(
                name: "Parametres");

            migrationBuilder.DropTable(
                name: "PiecesJointes");

            migrationBuilder.DropIndex(
                name: "IX_Meps_EstModele",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Approbations",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Bilan",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "BilanActive",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Environnement",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "EstModele",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "GoNoGo",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Sauvegarde_Mode",
                table: "Meps");

            migrationBuilder.AddColumn<Guid>(
                name: "SuppleantId",
                table: "Meps",
                type: "uuid",
                nullable: true);

            // Seul le premier suppléant peut être conservé.
            migrationBuilder.Sql("""UPDATE "Meps" SET "SuppleantId" = "SuppleantIds"[1];""");

            migrationBuilder.DropColumn(
                name: "SuppleantIds",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "TestsRecette",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "NonRetourAutoriseLe",
                table: "Etapes");

            migrationBuilder.DropColumn(
                name: "NonRetourAutoriseParId",
                table: "Etapes");

            migrationBuilder.DropColumn(
                name: "PointDeNonRetour",
                table: "Etapes");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Etapes");
        }
    }
}
