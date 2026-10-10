using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class SuiviResolutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ModifieeParId",
                table: "ResolutionsTypes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VersionsResolutionType",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolutionTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ModifieeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifieeParId = table.Column<Guid>(type: "uuid", nullable: true),
                    Titre = table.Column<string>(type: "text", nullable: false),
                    Symptomes = table.Column<string>(type: "text", nullable: false),
                    Diagnostic = table.Column<string>(type: "text", nullable: false),
                    SystemeCatalogueIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    Corrections = table.Column<string>(type: "jsonb", nullable: false),
                    VerificationInstruction = table.Column<string>(type: "text", nullable: false),
                    VerificationAttendu = table.Column<string>(type: "text", nullable: false),
                    Actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VersionsResolutionType", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VersionsResolutionType_ResolutionsTypes_ResolutionTypeId",
                        column: x => x.ResolutionTypeId,
                        principalTable: "ResolutionsTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VersionsResolutionType_ResolutionTypeId_Version",
                table: "VersionsResolutionType",
                columns: new[] { "ResolutionTypeId", "Version" });

            // Fiches existantes : leur état actuel devient la première version de l'historique (auteur inconnu).
            migrationBuilder.Sql("""
                INSERT INTO "VersionsResolutionType" ("Id", "ResolutionTypeId", "Version", "Action", "ModifieeLe", "ModifieeParId", "Titre",
                    "Symptomes", "Diagnostic", "SystemeCatalogueIds", "Corrections", "VerificationInstruction", "VerificationAttendu", "Actif")
                SELECT gen_random_uuid(), "Id", "Version", 'existante', "ModifieeLe", NULL, "Titre", "Symptomes", "Diagnostic",
                    "SystemeCatalogueIds", "Corrections", "VerificationInstruction", "VerificationAttendu", "Actif"
                FROM "ResolutionsTypes";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VersionsResolutionType");

            migrationBuilder.DropColumn(
                name: "ModifieeParId",
                table: "ResolutionsTypes");
        }
    }
}
