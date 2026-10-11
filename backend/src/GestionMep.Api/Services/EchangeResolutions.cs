using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMep.Api.Services;

/// <summary>Fichier d'échange des résolutions types (<c>gestion-mep resolutions exporter / importer</c>), version <see cref="EchangeResolutions.Format"/>.</summary>
public record ResolutionsExportees(int Format, string Application, DateTimeOffset ExporteeLe, List<ResolutionExportee>? Resolutions);

/// <summary>Résolution type exportée : les systèmes sont désignés par leur nom dans le catalogue, pour passer d'un serveur à l'autre.</summary>
public record ResolutionExportee(string? Titre, string? Symptomes, string? Diagnostic, List<string>? Systemes, List<CorrectionExportee>? Corrections,
    string? VerificationInstruction, string? VerificationAttendu);

public record CorrectionExportee(string? Instruction, string? ResultatAttendu);

/// <summary>Que faire d'une résolution importée dont le titre existe déjà.</summary>
public enum ModeImportResolutions { Refuser, Remplacer, Ignorer }

/// <summary>Résultat de l'importation d'une résolution : « creee », « remplacee », « ignoree » ou « refusee » (avec le motif).</summary>
public record ResultatImportResolution(string Titre, string Resultat, string? Motif = null);

/// <summary>
/// Exportation et importation des résolutions types. L'importation applique, résolution par résolution, les mêmes contrôles
/// que la saisie dans l'application (<see cref="ResolutionsEndpoints.Appliquer"/>) : une résolution refusée n'empêche pas les autres.
/// </summary>
public static class EchangeResolutions
{
    public const int Format = 1;

    /// <summary>Résolutions types actives, triées par titre, avec le nom de leurs systèmes.</summary>
    public static async Task<ResolutionsExportees> Exporter(MepDbContext db, DateTimeOffset maintenant)
    {
        var noms = await db.SystemesCatalogue.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Nom);
        var resolutions = await ResolutionsEndpoints.Lister(db, null);
        return new ResolutionsExportees(Format, VersionApplication.Courante, maintenant, resolutions.Select(r => new ResolutionExportee(
            r.Titre, r.Symptomes, r.Diagnostic, r.SystemeCatalogueIds.Where(noms.ContainsKey).Select(id => noms[id]).ToList(),
            r.Corrections.Select(c => new CorrectionExportee(c.Instruction, c.ResultatAttendu)).ToList(),
            r.VerificationInstruction, r.VerificationAttendu)).ToList());
    }

    /// <summary>
    /// Importe les résolutions (rien n'est écrit si <paramref name="essai"/>) ; le titre (sans tenir compte de la casse)
    /// identifie une résolution existante. Le bilan est inscrit dans le journal d'administration.
    /// </summary>
    public static async Task<List<ResultatImportResolution>> Importer(MepDbContext db, Acteur? acteur, ResolutionsExportees donnees,
        ModeImportResolutions mode, bool essai, string source, DateTimeOffset maintenant)
    {
        if (donnees.Format != Format) throw new ErreurHttp(1, $"Format de fichier {donnees.Format} non pris en charge (attendu : {Format}).");
        var catalogue = await db.SystemesCatalogue.AsNoTracking().ToListAsync();
        var existantes = await db.ResolutionsTypes.ToListAsync();
        var resultats = new List<ResultatImportResolution>();
        var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in donnees.Resolutions ?? [])
        {
            var titre = ServiceMep.Texte(r.Titre);
            var affiche = titre.Length > 0 ? titre : "(sans titre)";
            try
            {
                if (titre.Length > 0 && !vus.Add(titre)) throw ErreurHttp.Saisie("titre en double dans le fichier.");
                var ids = new List<Guid>();
                foreach (var nom in r.Systemes ?? [])
                {
                    var s = catalogue.FirstOrDefault(x => x.Nom.Equals(nom.Trim(), StringComparison.OrdinalIgnoreCase))
                        ?? throw ErreurHttp.Saisie($"système « {nom} » inconnu du catalogue (gestion-mep systemes ajouter).");
                    ids.Add(s.Id);
                }
                var saisie = new ResolutionTypeSaisie(null, r.Titre, r.Symptomes, r.Diagnostic, ids,
                    (r.Corrections ?? []).Select((c, i) => new CorrectionSaisie(i + 1, c.Instruction, c.ResultatAttendu)).ToList(),
                    r.VerificationInstruction, r.VerificationAttendu);
                // Contrôles de l'application sur une copie, avant de toucher à une résolution existante.
                await ResolutionsEndpoints.Appliquer(db, new ResolutionType(), saisie);

                var existante = existantes.FirstOrDefault(x => x.Titre.Equals(titre, StringComparison.OrdinalIgnoreCase));
                if (existante is null)
                {
                    if (!essai) await ResolutionsEndpoints.Creer(db, acteur, saisie, maintenant);
                    resultats.Add(new(titre, "creee"));
                }
                else if (mode == ModeImportResolutions.Ignorer) resultats.Add(new(titre, "ignoree", "titre déjà présent"));
                else if (mode == ModeImportResolutions.Refuser)
                    resultats.Add(new(titre, "refusee", "une résolution de ce titre existe déjà (--remplacer ou --ignorer-existantes)"));
                else
                {
                    if (!essai) await ResolutionsEndpoints.Remplacer(db, acteur, existante, saisie, maintenant);
                    resultats.Add(new(titre, "remplacee", existante.Actif ? null : "résolution désactivée, elle le reste"));
                }
            }
            catch (ErreurHttp e)
            {
                resultats.Add(new(affiche, "refusee", e.Message));
            }
        }
        var ecrites = resultats.Count(x => x.Resultat is "creee" or "remplacee");
        if (!essai && ecrites > 0)
        {
            JournalAdmin.Ecrire(db, acteur, "resolutions.importees", $"Résolutions types importées depuis {source} : "
                + $"{resultats.Count(x => x.Resultat == "creee")} créée(s), {resultats.Count(x => x.Resultat == "remplacee")} remplacée(s), "
                + $"{resultats.Count(x => x.Resultat == "ignoree")} ignorée(s), {resultats.Count(x => x.Resultat == "refusee")} refusée(s)");
            await db.SaveChangesAsync();
        }
        return resultats;
    }
}
