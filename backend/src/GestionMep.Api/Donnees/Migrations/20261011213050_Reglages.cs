using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class Reglages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reglages",
                columns: table => new
                {
                    Cle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Valeur = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ModifieLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiePar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reglages", x => x.Cle);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reglages");
        }
    }
}
