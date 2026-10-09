using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class Jalon31 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResponsablesTechniques",
                table: "Meps",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb"); // MEP existantes : aucun responsable technique
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponsablesTechniques",
                table: "Meps");
        }
    }
}
