using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class ConsoleAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Actif",
                table: "Utilisateurs",
                type: "boolean",
                nullable: false,
                // Comptes existants : actifs. Leur tampon de sécurité vide invalide les sessions ouvertes avant la mise à jour.
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "DoitChangerMotDePasse",
                table: "Utilisateurs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TamponSecurite",
                table: "Utilisateurs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "JournalAdmin",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Quand = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UtilisateurId = table.Column<Guid>(type: "uuid", nullable: true),
                    Auteur = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalAdmin", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalAdmin_Quand",
                table: "JournalAdmin",
                column: "Quand");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JournalAdmin");

            migrationBuilder.DropColumn(
                name: "Actif",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "DoitChangerMotDePasse",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "TamponSecurite",
                table: "Utilisateurs");
        }
    }
}
