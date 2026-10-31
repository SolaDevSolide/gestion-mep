using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class LiaisonNoosphere : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "SystemesCatalogue",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Liaisons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Produit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NomInstance = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    VersionPair = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Etat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConfirmeeIci = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmeeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmeePar = table.Column<string>(type: "text", nullable: false),
                    ConfirmeeParPair = table.Column<bool>(type: "boolean", nullable: true),
                    Capacites = table.Column<string>(type: "jsonb", nullable: false),
                    Cles = table.Column<string>(type: "jsonb", nullable: true),
                    AnciennesCles = table.Column<string>(type: "jsonb", nullable: true),
                    ClesPair = table.Column<string>(type: "jsonb", nullable: true),
                    AnciennesClesPair = table.Column<string>(type: "jsonb", nullable: true),
                    KidPair = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AncienKidPair = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Empreinte = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AppaireeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppaireePar = table.Column<string>(type: "text", nullable: false),
                    ClesRenouveleesLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DernierEchangeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CoupeeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CoupeePar = table.Column<string>(type: "text", nullable: false),
                    EnvoyerMeps = table.Column<bool>(type: "boolean", nullable: false),
                    SuiviAutomatique = table.Column<bool>(type: "boolean", nullable: false),
                    DomainesSuivis = table.Column<string>(type: "jsonb", nullable: false),
                    ApplicationsConnues = table.Column<string>(type: "jsonb", nullable: false),
                    EtagCatalogue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Catalogue = table.Column<string>(type: "jsonb", nullable: true),
                    DerniereSynchro = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DerniereErreur = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Liaisons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoncesLiaison",
                columns: table => new
                {
                    Nonce = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VuLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoncesLiaison", x => x.Nonce);
                });

            migrationBuilder.CreateTable(
                name: "DeploiementsLiaison",
                columns: table => new
                {
                    LiaisonId = table.Column<Guid>(type: "uuid", nullable: false),
                    MepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Empreinte = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Contenu = table.Column<string>(type: "jsonb", nullable: true),
                    EnvoyeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploiementsLiaison", x => new { x.LiaisonId, x.MepId });
                    table.ForeignKey(
                        name: "FK_DeploiementsLiaison_Liaisons_LiaisonId",
                        column: x => x.LiaisonId,
                        principalTable: "Liaisons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EchangesLiaison",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LiaisonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quand = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Sens = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Resultat = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: true),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EchangesLiaison", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EchangesLiaison_Liaisons_LiaisonId",
                        column: x => x.LiaisonId,
                        principalTable: "Liaisons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EchangesLiaison_LiaisonId_Quand",
                table: "EchangesLiaison",
                columns: new[] { "LiaisonId", "Quand" });

            migrationBuilder.CreateIndex(
                name: "IX_EchangesLiaison_Quand",
                table: "EchangesLiaison",
                column: "Quand");

            migrationBuilder.CreateIndex(
                name: "IX_Liaisons_AncienKidPair",
                table: "Liaisons",
                column: "AncienKidPair");

            migrationBuilder.CreateIndex(
                name: "IX_Liaisons_KidPair",
                table: "Liaisons",
                column: "KidPair");

            migrationBuilder.CreateIndex(
                name: "IX_NoncesLiaison_VuLe",
                table: "NoncesLiaison",
                column: "VuLe");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploiementsLiaison");

            migrationBuilder.DropTable(
                name: "EchangesLiaison");

            migrationBuilder.DropTable(
                name: "NoncesLiaison");

            migrationBuilder.DropTable(
                name: "Liaisons");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "SystemesCatalogue");
        }
    }
}
