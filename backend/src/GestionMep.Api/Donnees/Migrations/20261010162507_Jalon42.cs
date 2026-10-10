using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <summary>Jalon 4.2 : une résolution type peut concerner plusieurs systèmes du catalogue (l'ancien système est repris).</summary>
    public partial class Jalon42 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "SystemeCatalogueIds",
                table: "ResolutionsTypes",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.Sql("""UPDATE "ResolutionsTypes" SET "SystemeCatalogueIds" = ARRAY["SystemeCatalogueId"] WHERE "SystemeCatalogueId" IS NOT NULL;""");

            migrationBuilder.DropColumn(
                name: "SystemeCatalogueId",
                table: "ResolutionsTypes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SystemeCatalogueId",
                table: "ResolutionsTypes",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""UPDATE "ResolutionsTypes" SET "SystemeCatalogueId" = "SystemeCatalogueIds"[1];""");

            migrationBuilder.DropColumn(
                name: "SystemeCatalogueIds",
                table: "ResolutionsTypes");
        }
    }
}
