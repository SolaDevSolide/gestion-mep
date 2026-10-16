using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class RapportsFin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RapportsFin",
                columns: table => new
                {
                    MepId = table.Column<Guid>(type: "uuid", nullable: false),
                    FigeLe = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CalculeApres = table.Column<bool>(type: "boolean", nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    Contenu = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RapportsFin", x => x.MepId);
                    table.ForeignKey(
                        name: "FK_RapportsFin_Meps_MepId",
                        column: x => x.MepId,
                        principalTable: "Meps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RapportsFin");
        }
    }
}
