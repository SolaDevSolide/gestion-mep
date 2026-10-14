using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionMep.Api.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class EnvironnementsEtObjetsImpactes : Migration
    {
        /// <summary>
        /// Environnements et objets impactés par système (#54). Sans perte : l'ancien environnement de chaque MEP devient
        /// celui de chacun de ses systèmes et de chacune de ses étapes (il est aussi ajouté à la fiche du système au
        /// catalogue) ; la colonne « Environnement » de la MEP est gardée. Les anciens livrables deviennent des objets
        /// impactés de la catégorie « Packages », marqués « Repris » : pour un système du catalogue, l'objet est créé dans
        /// sa fiche puis relié ; sinon (système libre, sans système) il est gardé tel quel. La variable « livrables » des
        /// modèles de texte devient « objets_impactes ».
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var colonne in new[] { "Categories", "Environnements", "Objets" })
                migrationBuilder.AddColumn<string>(name: colonne, table: "SystemesCatalogue", type: "jsonb", nullable: false, defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "SystemesCatalogue",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(name: "Environnements", table: "Systemes", type: "jsonb", nullable: false, defaultValue: "[]");
            migrationBuilder.AddColumn<string>(name: "CategoriesObjets", table: "Parametres", type: "jsonb", nullable: false, defaultValue: "[]");
            migrationBuilder.AddColumn<string>(name: "Environnements", table: "Etapes", type: "jsonb", nullable: false, defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "ObjetsImpactes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MepId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObjetCatalogueId = table.Column<Guid>(type: "uuid", nullable: true),
                    Categorie = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Nom = table.Column<string>(type: "text", nullable: false),
                    Libelle = table.Column<string>(type: "text", nullable: false),
                    Precision = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Repris = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjetsImpactes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObjetsImpactes_Meps_MepId",
                        column: x => x.MepId,
                        principalTable: "Meps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Parametres",
                keyColumn: "Id",
                keyValue: 1,
                column: "CategoriesObjets",
                value: "[\"Code source\",\"Packages\",\"Flux\",\"Services\",\"Base de donn\\u00E9es\",\"Tables\",\"Colonnes\",\"Vues\",\"Proc\\u00E9dures stock\\u00E9es\",\"Fonctions\",\"Triggers\",\"Index\",\"R\\u00E9f\\u00E9rentiels\",\"Interfaces\",\"Configuration\",\"Infrastructure\",\"S\\u00E9curit\\u00E9\",\"API\",\"Fichiers d\\u0027\\u00E9change\",\"Ordonnancement\",\"Monitoring\",\"Sauvegarde / rollback\",\"Documentation technique et fonctionnelle\",\"Tests\",\"D\\u00E9pendances\",\"Certificats\",\"R\\u00E9seau\",\"Exploitation\",\"Tra\\u00E7abilit\\u00E9\"]");

            migrationBuilder.CreateIndex(
                name: "IX_ObjetsImpactes_MepId",
                table: "ObjetsImpactes",
                column: "MepId");

            // 1. Environnement de la MEP → environnement de chacun de ses systèmes, puis de chacune de ses étapes (sur tous ses systèmes).
            migrationBuilder.Sql("""
                UPDATE "Systemes" s
                SET "Environnements" = jsonb_build_array(jsonb_build_object(
                    'nom', btrim(m."Environnement"), 'production', lower(btrim(m."Environnement")) LIKE 'prod%'))
                FROM "Meps" m
                WHERE s."MepId" = m."Id" AND btrim(m."Environnement") <> '';
                """);
            migrationBuilder.Sql("""
                UPDATE "Etapes" e
                SET "Environnements" = COALESCE((
                    SELECT jsonb_agg(jsonb_build_object('systemeId', sid, 'nom', btrim(m."Environnement")) ORDER BY ord)
                    FROM unnest(e."SystemeIds") WITH ORDINALITY AS u(sid, ord)
                    WHERE EXISTS (SELECT 1 FROM "Systemes" s WHERE s."Id" = u.sid)), '[]'::jsonb)
                FROM "Meps" m
                WHERE e."MepId" = m."Id" AND btrim(m."Environnement") <> '';
                """);
            // 2. Environnements rencontrés → fiche du système au catalogue (un par nom, sans tenir compte de la casse).
            migrationBuilder.Sql("""
                UPDATE "SystemesCatalogue" c
                SET "Environnements" = x.envs
                FROM (
                    SELECT cid, jsonb_agg(jsonb_build_object('nom', nom, 'cible', '', 'production', lower(nom) LIKE 'prod%') ORDER BY nom) AS envs
                    FROM (
                        SELECT DISTINCT ON (s."CatalogueId", lower(btrim(m."Environnement"))) s."CatalogueId" AS cid, btrim(m."Environnement") AS nom
                        FROM "Systemes" s JOIN "Meps" m ON m."Id" = s."MepId"
                        WHERE s."CatalogueId" IS NOT NULL AND btrim(m."Environnement") <> ''
                        ORDER BY s."CatalogueId", lower(btrim(m."Environnement")), m."CreeLe"
                    ) d
                    GROUP BY cid
                ) x
                WHERE c."Id" = x.cid;
                """);
            // 3. Livrables → objets impactés « Packages », repris.
            migrationBuilder.Sql("""
                INSERT INTO "ObjetsImpactes" ("Id", "MepId", "SystemeId", "ObjetCatalogueId", "Categorie", "Type", "Nom", "Libelle", "Precision", "Version", "Repris")
                SELECT l."Id", l."MepId", l."SystemeId", NULL, 'Packages', 'Package', l."Objet", '', l."Emplacement", l."Version", true
                FROM "Livrables" l;
                """);
            // 4. Objets des systèmes du catalogue : créés dans la fiche (catégorie « Packages », un par nom), puis reliés.
            migrationBuilder.Sql("""
                UPDATE "SystemesCatalogue" c
                SET "Objets" = c."Objets" || x.objets,
                    "Categories" = CASE WHEN c."Categories" @> '["Packages"]'::jsonb THEN c."Categories" ELSE c."Categories" || '["Packages"]'::jsonb END
                FROM (
                    SELECT cid, jsonb_agg(jsonb_build_object('id', gen_random_uuid(), 'categorie', 'Packages', 'type', 'Package', 'nom', nom,
                        'libelle', '', 'precision', prec) ORDER BY nom) AS objets
                    FROM (
                        SELECT DISTINCT ON (s."CatalogueId", lower(o."Nom")) s."CatalogueId" AS cid, o."Nom" AS nom, o."Precision" AS prec
                        FROM "ObjetsImpactes" o JOIN "Systemes" s ON s."Id" = o."SystemeId"
                        WHERE s."CatalogueId" IS NOT NULL AND btrim(o."Nom") <> ''
                        ORDER BY s."CatalogueId", lower(o."Nom"), o."Id"
                    ) d
                    GROUP BY cid
                ) x
                WHERE c."Id" = x.cid;
                """);
            migrationBuilder.Sql("""
                UPDATE "ObjetsImpactes" o
                SET "ObjetCatalogueId" = (x.objet->>'id')::uuid
                FROM "Systemes" s
                JOIN "SystemesCatalogue" c ON c."Id" = s."CatalogueId"
                CROSS JOIN LATERAL jsonb_array_elements(c."Objets") AS x(objet)
                WHERE s."Id" = o."SystemeId" AND x.objet->>'categorie' = 'Packages' AND lower(x.objet->>'nom') = lower(o."Nom");
                """);
            // 5. Modèles de texte des équipes : {{livrables}} → {{objets_impactes}}.
            migrationBuilder.Sql("""
                UPDATE "ModelesTexte"
                SET "Objet" = replace("Objet", '{{livrables}}', '{{objets_impactes}}'),
                    "Corps" = replace("Corps", '{{livrables}}', '{{objets_impactes}}')
                WHERE "Objet" LIKE '%{{livrables}}%' OR "Corps" LIKE '%{{livrables}}%';
                """);

            migrationBuilder.DropTable(
                name: "Livrables");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Livrables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Emplacement = table.Column<string>(type: "text", nullable: false),
                    MepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Objet = table.Column<string>(type: "text", nullable: false),
                    SystemeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Livrables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Livrables_Meps_MepId",
                        column: x => x.MepId,
                        principalTable: "Meps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Livrables_MepId",
                table: "Livrables",
                column: "MepId");

            // Les objets impactés redeviennent des livrables (nom, version, précision comme emplacement).
            migrationBuilder.Sql("""
                INSERT INTO "Livrables" ("Id", "MepId", "SystemeId", "Objet", "Version", "Emplacement")
                SELECT "Id", "MepId", "SystemeId", "Nom", "Version", "Precision" FROM "ObjetsImpactes";
                """);

            migrationBuilder.DropTable(
                name: "ObjetsImpactes");

            migrationBuilder.Sql("""
                UPDATE "ModelesTexte"
                SET "Objet" = replace("Objet", '{{objets_impactes}}', '{{livrables}}'),
                    "Corps" = replace("Corps", '{{objets_impactes}}', '{{livrables}}')
                WHERE "Objet" LIKE '%{{objets_impactes}}%' OR "Corps" LIKE '%{{objets_impactes}}%';
                """);

            foreach (var colonne in new[] { "Categories", "Environnements", "Objets", "Version" })
                migrationBuilder.DropColumn(name: colonne, table: "SystemesCatalogue");
            migrationBuilder.DropColumn(name: "Environnements", table: "Systemes");
            migrationBuilder.DropColumn(name: "CategoriesObjets", table: "Parametres");
            migrationBuilder.DropColumn(name: "Environnements", table: "Etapes");
        }
    }
}
