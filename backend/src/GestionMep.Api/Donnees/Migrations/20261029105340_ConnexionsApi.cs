using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class ConnexionsApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnexionsApi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Modele = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Methode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    UrlSecrete = table.Column<string>(type: "jsonb", nullable: true),
                    OrigineSecrets = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Authentification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Utilisateur = table.Column<string>(type: "text", nullable: false),
                    NomEntete = table.Column<string>(type: "text", nullable: false),
                    TokenUrl = table.Column<string>(type: "text", nullable: false),
                    ClientId = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Secret = table.Column<string>(type: "jsonb", nullable: true),
                    Entetes = table.Column<string>(type: "jsonb", nullable: false),
                    SecretSignature = table.Column<string>(type: "jsonb", nullable: true),
                    Format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Corps = table.Column<string>(type: "text", nullable: false),
                    Champs = table.Column<string>(type: "jsonb", nullable: false),
                    DelaiSecondes = table.Column<int>(type: "integer", nullable: false),
                    Tentatives = table.Column<int>(type: "integer", nullable: false),
                    Declencheurs = table.Column<string>(type: "jsonb", nullable: false),
                    Filtres = table.Column<string>(type: "jsonb", nullable: false),
                    EnvoiManuel = table.Column<bool>(type: "boolean", nullable: false),
                    LibelleEnvoiManuel = table.Column<string>(type: "text", nullable: false),
                    CreeeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifieeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifieePar = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnexionsApi", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppelsApi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnexionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MepId = table.Column<Guid>(type: "uuid", nullable: true),
                    MepNom = table.Column<string>(type: "text", nullable: false),
                    EvenementId = table.Column<long>(type: "bigint", nullable: true),
                    TypeEvenement = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LibelleEvenement = table.Column<string>(type: "text", nullable: false),
                    Resume = table.Column<string>(type: "text", nullable: false),
                    Origine = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AppelOrigineId = table.Column<Guid>(type: "uuid", nullable: true),
                    DemandeParId = table.Column<Guid>(type: "uuid", nullable: true),
                    DemandePar = table.Column<string>(type: "text", nullable: false),
                    Methode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    UrlSecrete = table.Column<bool>(type: "boolean", nullable: false),
                    TypeContenu = table.Column<string>(type: "text", nullable: false),
                    Corps = table.Column<string>(type: "text", nullable: false),
                    Entetes = table.Column<string>(type: "jsonb", nullable: false),
                    Etat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TentativesMax = table.Column<int>(type: "integer", nullable: false),
                    DelaiSecondes = table.Column<int>(type: "integer", nullable: false),
                    Tentatives = table.Column<string>(type: "jsonb", nullable: false),
                    ProchaineTentative = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TermineLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DernierCode = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppelsApi", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppelsApi_ConnexionsApi_ConnexionId",
                        column: x => x.ConnexionId,
                        principalTable: "ConnexionsApi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppelsApi_Journal_EvenementId",
                        column: x => x.EvenementId,
                        principalTable: "Journal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AppelsApi_Meps_MepId",
                        column: x => x.MepId,
                        principalTable: "Meps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppelsApi_ConnexionId_MepId_CreeLe",
                table: "AppelsApi",
                columns: new[] { "ConnexionId", "MepId", "CreeLe" });

            migrationBuilder.CreateIndex(
                name: "IX_AppelsApi_CreeLe",
                table: "AppelsApi",
                column: "CreeLe");

            migrationBuilder.CreateIndex(
                name: "IX_AppelsApi_Etat_ProchaineTentative",
                table: "AppelsApi",
                columns: new[] { "Etat", "ProchaineTentative" });

            migrationBuilder.CreateIndex(
                name: "IX_AppelsApi_EvenementId",
                table: "AppelsApi",
                column: "EvenementId");

            migrationBuilder.CreateIndex(
                name: "IX_AppelsApi_MepId_CreeLe",
                table: "AppelsApi",
                columns: new[] { "MepId", "CreeLe" });

            migrationBuilder.CreateIndex(
                name: "IX_ConnexionsApi_Nom",
                table: "ConnexionsApi",
                column: "Nom",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppelsApi");

            migrationBuilder.DropTable(
                name: "ConnexionsApi");
        }
    }
}
