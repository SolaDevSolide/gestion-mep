using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class JetonsApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EstTechnique",
                table: "Utilisateurs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "JetonsApi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UtilisateurId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prefixe = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Empreinte = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Portees = table.Column<string>(type: "jsonb", nullable: false),
                    CreeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreeParId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreePar = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ExpireLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevoqueLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevoquePar = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DerniereUtilisationLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DerniereAdresseIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JetonsApi", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JetonsApi_Utilisateurs_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JetonsApi_Empreinte",
                table: "JetonsApi",
                column: "Empreinte",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JetonsApi_UtilisateurId",
                table: "JetonsApi",
                column: "UtilisateurId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JetonsApi");

            migrationBuilder.DropColumn(
                name: "EstTechnique",
                table: "Utilisateurs");
        }
    }
}
