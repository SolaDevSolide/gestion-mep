using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class SectionsEtGrapheLibre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Graphe libre (#76) et sections du plan (#60) : colonnes ajoutées, aucune donnée modifiée. Les préalables des
            // étapes existantes restent tels quels (tous directs, aucun dérivé) : l'exécution ne change pas.
            migrationBuilder.AddColumn<string>(name: "Sections", table: "Meps", type: "jsonb", nullable: false, defaultValue: "[]");
            migrationBuilder.AddColumn<List<Guid>>(name: "DependancesDerivees", table: "Etapes", type: "uuid[]", nullable: false,
                defaultValueSql: "'{}'::uuid[]");
            migrationBuilder.AddColumn<List<Guid>>(name: "PortsAttendus", table: "Etapes", type: "uuid[]", nullable: false,
                defaultValueSql: "'{}'::uuid[]");
            migrationBuilder.AddColumn<Guid>(name: "SectionId", table: "Etapes", type: "uuid", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sections",
                table: "Meps");

            migrationBuilder.DropColumn(
                name: "DependancesDerivees",
                table: "Etapes");

            migrationBuilder.DropColumn(
                name: "PortsAttendus",
                table: "Etapes");

            migrationBuilder.DropColumn(
                name: "SectionId",
                table: "Etapes");
        }
    }
}
