using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class Initiale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Meps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Outil = table.Column<string>(type: "text", nullable: false),
                    VersionActuelle = table.Column<string>(type: "text", nullable: false),
                    VersionCible = table.Column<string>(type: "text", nullable: false),
                    Objet = table.Column<string>(type: "text", nullable: false),
                    Impacts = table.Column<string>(type: "text", nullable: false),
                    DateMep = table.Column<DateOnly>(type: "date", nullable: true),
                    HeureDebut = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    HeureFin = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    FuseauHoraire = table.Column<string>(type: "text", nullable: false),
                    IndisponibiliteMinutes = table.Column<int>(type: "integer", nullable: true),
                    PiloteId = table.Column<Guid>(type: "uuid", nullable: true),
                    SuppleantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    EcheanceRepli = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Sauvegarde_Structure = table.Column<bool>(type: "boolean", nullable: false),
                    Sauvegarde_Donnees = table.Column<bool>(type: "boolean", nullable: false),
                    Sauvegarde_Configuration = table.Column<bool>(type: "boolean", nullable: false),
                    Sauvegarde_ResponsableId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sauvegarde_RealiseeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Sauvegarde_Procedure = table.Column<string>(type: "text", nullable: false),
                    Sauvegarde_Copies = table.Column<string>(type: "text", nullable: false),
                    Sauvegarde_RestaurationTestee = table.Column<bool>(type: "boolean", nullable: false),
                    Sauvegarde_DateTestRestauration = table.Column<DateOnly>(type: "date", nullable: true),
                    Sauvegarde_PreuveTestRestauration = table.Column<string>(type: "text", nullable: false),
                    ProcedureRelueParId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcedureRelueLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DemarrageAutoriseParId = table.Column<Guid>(type: "uuid", nullable: true),
                    DemarrageAutoriseLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreeParUtilisateurId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Utilisateurs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Login = table.Column<string>(type: "text", nullable: false),
                    NomAffiche = table.Column<string>(type: "text", nullable: false),
                    MotDePasseHash = table.Column<string>(type: "text", nullable: false),
                    EstAdmin = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Utilisateurs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Etapes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Ordre = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    SystemeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponsableId = table.Column<Guid>(type: "uuid", nullable: true),
                    Criticite = table.Column<int>(type: "integer", nullable: true),
                    Etat = table.Column<int>(type: "integer", nullable: false),
                    Instructions = table.Column<string>(type: "text", nullable: false),
                    ResultatAttendu = table.Column<string>(type: "text", nullable: false),
                    Preuve = table.Column<string>(type: "text", nullable: false),
                    DebutReel = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinReelle = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Dependances = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Etapes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Etapes_Meps_MepId",
                        column: x => x.MepId,
                        principalTable: "Meps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Journal",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quand = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UtilisateurId = table.Column<Guid>(type: "uuid", nullable: true),
                    Auteur = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Journal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Journal_Meps_MepId",
                        column: x => x.MepId,
                        principalTable: "Meps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Livrables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Objet = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Emplacement = table.Column<string>(type: "text", nullable: false),
                    SystemeId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Livrables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Livrables_Meps_MepId",
                        column: x => x.MepId,
                        principalTable: "Meps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Participants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Contact = table.Column<string>(type: "text", nullable: false),
                    UtilisateurId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Participants_Meps_MepId",
                        column: x => x.MepId,
                        principalTable: "Meps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Systemes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "text", nullable: false),
                    Cible = table.Column<string>(type: "text", nullable: false),
                    Impact = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Systemes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Systemes_Meps_MepId",
                        column: x => x.MepId,
                        principalTable: "Meps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Etapes_MepId_Code",
                table: "Etapes",
                columns: new[] { "MepId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Journal_MepId_Id",
                table: "Journal",
                columns: new[] { "MepId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Livrables_MepId",
                table: "Livrables",
                column: "MepId");

            migrationBuilder.CreateIndex(
                name: "IX_Meps_CreeLe",
                table: "Meps",
                column: "CreeLe");

            migrationBuilder.CreateIndex(
                name: "IX_Participants_MepId",
                table: "Participants",
                column: "MepId");

            migrationBuilder.CreateIndex(
                name: "IX_Systemes_MepId",
                table: "Systemes",
                column: "MepId");

            migrationBuilder.CreateIndex(
                name: "IX_Utilisateurs_Login",
                table: "Utilisateurs",
                column: "Login",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Etapes");

            migrationBuilder.DropTable(
                name: "Journal");

            migrationBuilder.DropTable(
                name: "Livrables");

            migrationBuilder.DropTable(
                name: "Participants");

            migrationBuilder.DropTable(
                name: "Systemes");

            migrationBuilder.DropTable(
                name: "Utilisateurs");

            migrationBuilder.DropTable(
                name: "Meps");
        }
    }
}
