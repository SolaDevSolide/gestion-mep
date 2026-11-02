using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class ApprobateursDesPeriodesDeGel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Approbateurs des dérogations par période (#129) : vides pour les périodes existantes, les administrateurs décident.
            migrationBuilder.AddColumn<List<Guid>>(
                name: "ApprobateurIds",
                table: "PeriodesGel",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "EquipeApprobatriceIds",
                table: "PeriodesGel",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprobateurIds",
                table: "PeriodesGel");

            migrationBuilder.DropColumn(
                name: "EquipeApprobatriceIds",
                table: "PeriodesGel");
        }
    }
}
