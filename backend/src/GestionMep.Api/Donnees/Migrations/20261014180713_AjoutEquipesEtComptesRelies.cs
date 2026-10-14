using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class AjoutEquipesEtComptesRelies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ComptePrincipalId",
                table: "Utilisateurs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Equipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nom = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    MembreIds = table.Column<string>(type: "jsonb", nullable: false),
                    CreeeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Utilisateurs_ComptePrincipalId",
                table: "Utilisateurs",
                column: "ComptePrincipalId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipes_Nom",
                table: "Equipes",
                column: "Nom",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Utilisateurs_Utilisateurs_ComptePrincipalId",
                table: "Utilisateurs",
                column: "ComptePrincipalId",
                principalTable: "Utilisateurs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Utilisateurs_Utilisateurs_ComptePrincipalId",
                table: "Utilisateurs");

            migrationBuilder.DropTable(
                name: "Equipes");

            migrationBuilder.DropIndex(
                name: "IX_Utilisateurs_ComptePrincipalId",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "ComptePrincipalId",
                table: "Utilisateurs");
        }
    }
}
