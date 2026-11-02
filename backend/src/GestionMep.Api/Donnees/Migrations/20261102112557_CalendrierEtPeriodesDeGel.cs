using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class CalendrierEtPeriodesDeGel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Créneau du calendrier : colonnes vides (format 0), calculées au premier démarrage par Amorcage.RecalculerCalendrier.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Calendrier_Debut",
                table: "Meps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Calendrier_Fin",
                table: "Meps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Calendrier_Format",
                table: "Meps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Calendrier_JourneeEntiere",
                table: "Meps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DerogationsGel",
                table: "Meps",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "AbonnementsCalendrier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UtilisateurId = table.Column<Guid>(type: "uuid", nullable: false),
                    Empreinte = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DernierAcces = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbonnementsCalendrier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbonnementsCalendrier_Utilisateurs_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PeriodesGel",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Debut = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Motif = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SystemeCatalogueIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    Environnements = table.Column<string>(type: "jsonb", nullable: false),
                    ProductionSeulement = table.Column<bool>(type: "boolean", nullable: false),
                    Recurrence = table.Column<int>(type: "integer", nullable: false),
                    FinRecurrence = table.Column<DateOnly>(type: "date", nullable: true),
                    FuseauHoraire = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreeeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifieeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodesGel", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Meps_Calendrier_Debut",
                table: "Meps",
                column: "Calendrier_Debut");

            migrationBuilder.CreateIndex(
                name: "IX_AbonnementsCalendrier_Empreinte",
                table: "AbonnementsCalendrier",
                column: "Empreinte",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbonnementsCalendrier_UtilisateurId",
                table: "AbonnementsCalendrier",
                column: "UtilisateurId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodesGel_Debut",
                table: "PeriodesGel",
                column: "Debut");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbonnementsCalendrier");

            migrationBuilder.DropTable(
                name: "PeriodesGel");

            migrationBuilder.DropIndex(
                name: "IX_Meps_Calendrier_Debut",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Calendrier_Debut",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Calendrier_Fin",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Calendrier_Format",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "Calendrier_JourneeEntiere",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "DerogationsGel",
                table: "Meps");
        }
    }
}
