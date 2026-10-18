using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class IndexRechercheAvancee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Systemes_CatalogueId",
                table: "Systemes",
                column: "CatalogueId");

            migrationBuilder.CreateIndex(
                name: "IX_Participants_UtilisateurId",
                table: "Participants",
                column: "UtilisateurId");

            migrationBuilder.CreateIndex(
                name: "IX_Journal_Type_MepId",
                table: "Journal",
                columns: new[] { "Type", "MepId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Systemes_CatalogueId",
                table: "Systemes");

            migrationBuilder.DropIndex(
                name: "IX_Participants_UtilisateurId",
                table: "Participants");

            migrationBuilder.DropIndex(
                name: "IX_Journal_Type_MepId",
                table: "Journal");
        }
    }
}
