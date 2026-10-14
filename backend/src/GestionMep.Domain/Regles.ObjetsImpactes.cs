namespace GestionMep.Domain;

// Règles métier : objets impactés d'une MEP, choisis dans la fiche de chaque système au catalogue.
public static partial class Regles
{
    /// <summary>Bilan d'un choix ou d'un import d'objets impactés.</summary>
    public record BilanObjets(int Ajoutes, int Modifies, int Retires, int Inchanges);

    private static ObjetImpacte Copie(Mep mep, Systeme systeme, ObjetCatalogue o, string version, bool repris) => new()
    {
        MepId = mep.Id, SystemeId = systeme.Id, ObjetCatalogueId = o.Id, Categorie = o.Categorie, Type = o.Type, Nom = o.Nom,
        Libelle = o.Libelle, Precision = o.Precision, Version = version, Repris = repris,
    };

    private static void ExigerSystemeDuCatalogue(Systeme systeme, SystemeCatalogue catalogue)
    {
        if (systeme.CatalogueId != catalogue.Id)
            throw new RegleMetierException($"{systeme.Nom} n'est pas issu du catalogue : ses objets impactés se choisissent dans la fiche d'un système du catalogue.");
    }

    /// <summary>
    /// Objets impactés d'un système (préparation uniquement) : <paramref name="choix"/> remplace tous les objets du catalogue
    /// déjà retenus pour ce système (copie à jour de la fiche, version reçue). Un objet retiré du catalogue depuis peut être
    /// gardé tel quel ; les anciens livrables hors catalogue ne sont pas touchés (ils se retirent un par un).
    /// </summary>
    public static BilanObjets DefinirObjetsSysteme(Mep mep, Systeme systeme, SystemeCatalogue catalogue, IEnumerable<(Guid ObjetId, string Version)> choix)
    {
        ExigerPreparation(mep);
        ExigerSystemeDuCatalogue(systeme, catalogue);
        var actuels = mep.ObjetsImpactes.Where(o => o.SystemeId == systeme.Id && o.ObjetCatalogueId is not null).ToList();
        var retenus = new HashSet<Guid>();
        int ajoutes = 0, modifies = 0, inchanges = 0;
        foreach (var (id, brut) in choix)
        {
            if (!retenus.Add(id)) continue;
            var version = (brut ?? "").Trim();
            if (version.Length > 200) throw new RegleMetierException("Version ou correction : 200 caractères au plus.");
            var existant = actuels.FirstOrDefault(o => o.ObjetCatalogueId == id);
            var fiche = catalogue.Objets.FirstOrDefault(o => o.Id == id);
            if (existant is null)
            {
                if (fiche is null) throw new RegleMetierException($"Objet inconnu dans la fiche {catalogue.Nom} du catalogue.");
                mep.ObjetsImpactes.Add(Copie(mep, systeme, fiche, version, repris: false));
                ajoutes++;
                continue;
            }
            var change = existant.Version != version;
            if (fiche is not null)
            {
                change |= existant.Categorie != fiche.Categorie || existant.Type != fiche.Type || existant.Nom != fiche.Nom
                    || existant.Libelle != fiche.Libelle || existant.Precision != fiche.Precision;
                (existant.Categorie, existant.Type, existant.Nom, existant.Libelle, existant.Precision) = (fiche.Categorie, fiche.Type, fiche.Nom, fiche.Libelle, fiche.Precision);
            }
            existant.Version = version;
            if (change) modifies++;
            else inchanges++;
        }
        var retires = actuels.Where(o => !retenus.Contains(o.ObjetCatalogueId!.Value)).ToList();
        foreach (var o in retires) mep.ObjetsImpactes.Remove(o);
        return new BilanObjets(ajoutes, modifies, retires.Count, inchanges);
    }

    /// <summary>Ligne d'un import d'objets impactés, déjà rattachée à un objet du catalogue.</summary>
    public record LigneObjet(Systeme Systeme, ObjetCatalogue Objet, string Version);

    /// <summary>
    /// Import (préparation uniquement) : ajoute les objets absents et met à jour la version des objets déjà retenus. Rien
    /// n'est retiré : un objet absent du fichier reste dans la MEP. La dernière ligne d'un même objet l'emporte.
    /// </summary>
    public static BilanObjets ImporterObjets(Mep mep, IEnumerable<LigneObjet> lignes)
    {
        ExigerPreparation(mep);
        int ajoutes = 0, modifies = 0, inchanges = 0;
        var parObjet = lignes.GroupBy(l => (l.Systeme.Id, l.Objet.Id)).Select(g => g.Last());
        foreach (var l in parObjet)
        {
            var existant = mep.ObjetsImpactes.FirstOrDefault(o => o.SystemeId == l.Systeme.Id && o.ObjetCatalogueId == l.Objet.Id);
            if (existant is null)
            {
                mep.ObjetsImpactes.Add(Copie(mep, l.Systeme, l.Objet, l.Version.Trim(), repris: false));
                ajoutes++;
            }
            else if (existant.Version != l.Version.Trim())
            {
                existant.Version = l.Version.Trim();
                modifies++;
            }
            else inchanges++;
        }
        return new BilanObjets(ajoutes, modifies, 0, inchanges);
    }

    /// <summary>
    /// Objets impactés dans l'ordre d'affichage : par système de la MEP, puis par catégorie (ordre de la fiche du catalogue
    /// quand elle est fournie), puis par nom.
    /// </summary>
    public static IEnumerable<ObjetImpacte> ObjetsTries(Mep mep, IReadOnlyDictionary<Guid, SystemeCatalogue>? catalogue = null)
    {
        int RangSysteme(Guid? id) => id is { } s && mep.Systemes.FindIndex(x => x.Id == s) is var i and >= 0 ? i : int.MaxValue;
        int RangCategorie(ObjetImpacte o)
        {
            var cat = mep.Systemes.FirstOrDefault(s => s.Id == o.SystemeId)?.CatalogueId is { } c && catalogue is not null && catalogue.TryGetValue(c, out var fiche)
                ? fiche.Categories.FindIndex(x => x == o.Categorie) : -1;
            return cat < 0 ? int.MaxValue : cat;
        }
        return mep.ObjetsImpactes.OrderBy(o => RangSysteme(o.SystemeId)).ThenBy(RangCategorie).ThenBy(o => o.Categorie, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Nom, StringComparer.OrdinalIgnoreCase);
    }
}
