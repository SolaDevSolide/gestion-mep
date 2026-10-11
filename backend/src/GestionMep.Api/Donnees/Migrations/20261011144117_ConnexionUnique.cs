using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class ConnexionUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfigurationsSso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Fournisseur = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Annuaire = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Autorite = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SecretChiffre = table.Column<string>(type: "text", nullable: false),
                    SecretExpireLe = table.Column<DateOnly>(type: "date", nullable: true),
                    CreationAutomatique = table.Column<bool>(type: "boolean", nullable: false),
                    RoleAdmin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Libelle = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ModifieeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationsSso", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentitesExternes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UtilisateurId = table.Column<Guid>(type: "uuid", nullable: false),
                    Emetteur = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Sujet = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Identifiant = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreeeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DerniereConnexion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentitesExternes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentitesExternes_Utilisateurs_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ConfigurationsSso",
                columns: new[] { "Id", "Active", "Annuaire", "Autorite", "ClientId", "CreationAutomatique", "Fournisseur", "Libelle", "ModifieeLe", "RoleAdmin", "SecretChiffre", "SecretExpireLe" },
                values: new object[] { 1, false, "", "", "", true, "Microsoft", "", null, "", "", null });

            migrationBuilder.CreateIndex(
                name: "IX_IdentitesExternes_Emetteur_Sujet",
                table: "IdentitesExternes",
                columns: new[] { "Emetteur", "Sujet" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentitesExternes_UtilisateurId",
                table: "IdentitesExternes",
                column: "UtilisateurId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfigurationsSso");

            migrationBuilder.DropTable(
                name: "IdentitesExternes");
        }
    }
}
