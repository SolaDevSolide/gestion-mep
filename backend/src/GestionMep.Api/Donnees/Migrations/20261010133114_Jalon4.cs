using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <summary>
    /// Jalon 4 : profils des comptes, sauvegarde par système, fiches de recette, étapes sur plusieurs systèmes et graphe par lignes.
    /// Les données existantes sont reprises ici en SQL ; les lignes du graphe (calcul récursif) sont calculées au démarrage
    /// par <c>Amorcage</c> pour les étapes marquées <c>Ligne = -1</c>.
    /// </summary>
    public partial class Jalon4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- Comptes : rôle, contact, expertises ----------
            migrationBuilder.AddColumn<string>(
                name: "Contact",
                table: "Utilisateurs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Expertises",
                table: "Utilisateurs",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Utilisateurs",
                type: "text",
                nullable: false,
                defaultValue: "");

            // ---------- Sauvegarde par système ----------
            migrationBuilder.AddColumn<string>(
                name: "Sauvegarde_Contenu",
                table: "Systemes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Sauvegarde_Justification",
                table: "Systemes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Sauvegarde_Moment",
                table: "Systemes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Systèmes existants : à renseigner (0).
            migrationBuilder.AddColumn<int>(
                name: "Sauvegarde_Statut",
                table: "Systemes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Ancien mode global « pendant la MEP » (1) : repris comme moment de chaque système.
            migrationBuilder.Sql("""
                UPDATE "Systemes" s SET "Sauvegarde_Moment" = 1
                FROM "Meps" m WHERE m."Id" = s."MepId" AND m."Sauvegarde_Mode" = 1;
                """);

            // ---------- Étapes : plusieurs systèmes, ligne, durée ----------
            migrationBuilder.AddColumn<int>(
                name: "DureeEstimeeMinutes",
                table: "Etapes",
                type: "integer",
                nullable: true);

            // -1 : ligne à calculer au démarrage (Amorcage) d'après les dépendances.
            migrationBuilder.AddColumn<int>(
                name: "Ligne",
                table: "Etapes",
                type: "integer",
                nullable: false,
                defaultValue: -1);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "SystemeIds",
                table: "Etapes",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.Sql("""UPDATE "Etapes" SET "SystemeIds" = ARRAY["SystemeId"] WHERE "SystemeId" IS NOT NULL;""");

            migrationBuilder.DropColumn(
                name: "SystemeId",
                table: "Etapes");

            // ---------- Recette : les anciens tests deviennent une fiche « Recette » sans système ----------
            migrationBuilder.RenameColumn(
                name: "TestsRecette",
                table: "Meps",
                newName: "FichesRecette");

            migrationBuilder.Sql("""
                UPDATE "Meps" SET "FichesRecette" = jsonb_build_array(jsonb_build_object(
                    'id', gen_random_uuid(), 'ordre', 1, 'systemeId', NULL, 'titre', 'Recette',
                    'etapes', (
                        SELECT jsonb_agg(jsonb_build_object(
                            'id', x.t->'id', 'ordre', x.n,
                            'instructions', COALESCE(x.t->'libelle', '""'::jsonb),
                            'resultatAttendu', COALESCE(x.t->'resultatAttendu', '""'::jsonb),
                            'resultat', x.t->'resultat',
                            'observation', COALESCE(x.t->'observation', '""'::jsonb),
                            'parId', x.t->'parId', 'le', x.t->'le') ORDER BY x.n)
                        FROM (SELECT e AS t, row_number() OVER (ORDER BY (e->>'ordre')::int) AS n
                              FROM jsonb_array_elements("Meps"."FichesRecette") e) x)))
                WHERE jsonb_array_length("FichesRecette") > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Retour arrière du schéma seulement : les fiches de recette, lignes et systèmes multiples ne sont pas reconvertis.
            migrationBuilder.DropColumn(
                name: "Contact",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "Expertises",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "Sauvegarde_Contenu",
                table: "Systemes");

            migrationBuilder.DropColumn(
                name: "Sauvegarde_Justification",
                table: "Systemes");

            migrationBuilder.DropColumn(
                name: "Sauvegarde_Moment",
                table: "Systemes");

            migrationBuilder.DropColumn(
                name: "Sauvegarde_Statut",
                table: "Systemes");

            migrationBuilder.DropColumn(
                name: "DureeEstimeeMinutes",
                table: "Etapes");

            migrationBuilder.DropColumn(
                name: "Ligne",
                table: "Etapes");

            migrationBuilder.AddColumn<Guid>(
                name: "SystemeId",
                table: "Etapes",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""UPDATE "Etapes" SET "SystemeId" = "SystemeIds"[1];""");

            migrationBuilder.DropColumn(
                name: "SystemeIds",
                table: "Etapes");

            migrationBuilder.RenameColumn(
                name: "FichesRecette",
                table: "Meps",
                newName: "TestsRecette");

            migrationBuilder.Sql("""UPDATE "Meps" SET "TestsRecette" = '[]'::jsonb;""");
        }
    }
}
