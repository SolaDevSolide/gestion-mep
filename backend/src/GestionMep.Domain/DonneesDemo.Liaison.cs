using System.Text.Json;

namespace GestionMep.Domain;

/// <summary>
/// Liaison fictive de la démonstration avec « Noosphere Alizé (démonstration) », à l'adresse
/// <c>https://noosphere.demo.invalid</c> : active, confirmée des deux côtés, deux systèmes suivis (API et WEB) et quelques
/// échanges au journal. Aucun appel ne part : les échanges sont notés « demo » et le catalogue est celui-ci.
/// </summary>
public static partial class DonneesDemo
{
    public const string UrlNoosphereDemo = "https://noosphere.demo.invalid";

    public static CatalogueNoosphere CatalogueNoosphere(string? url = null)
    {
        var u = (url ?? UrlNoosphereDemo).TrimEnd('/');
        ApplicationNoosphere App(string id, string nom, string description, string domaine, string equipe, string type, string cycle, string tech) =>
            new(id, nom, description, domaine, equipe, type, cycle, JsonSerializer.SerializeToElement(tech), $"{u}/applications/{id}");
        return new CatalogueNoosphere(null, "Noosphere Alizé (démonstration)", u,
            [new("commerce", "Commerce"), new("finance", "Finance"), new("rh", "Ressources humaines")],
            [new("portail", "Équipe Portail", "portail@exemple.invalid"), new("finance", "Équipe Finance", "finance@exemple.invalid"),
                new("rh", "Équipe RH", "rh@exemple.invalid")],
            [
                App("api-commandes", "API", "Service API de test", "commerce", "portail", "app", "live", "ASP.NET"),
                App("portail-web", "WEB", "Interface web de test", "commerce", "portail", "app", "live", "React"),
                App("ancien-extranet", "Ancien extranet", "Extranet remplacé par le portail", "commerce", "portail", "app", "retired", "PHP"),
                App("facturation", "Facturation", "Émission et suivi des factures clients", "finance", "finance", "app", "live", "Java"),
                App("tresorerie", "Trésorerie", "Prévisions de trésorerie", "finance", "finance", "saas", "planned", "SaaS"),
                App("annuaire-rh", "Annuaire RH", "Annuaire des collaborateurs", "rh", "rh", "app", "retiring", "LDAP"),
            ],
            [
                new("flux-commandes", "Commandes du portail", "portail-web", "api-commandes", "api", "live", default, $"{u}/flows/flux-commandes"),
                new("flux-factures", "Factures des commandes", "api-commandes", "facturation", "file", "live", JsonSerializer.SerializeToElement("0 2 * * *"),
                    $"{u}/flows/flux-factures"),
            ]);
    }

    /// <summary>
    /// Liaison de démonstration : clés publiques fictives (aucune clé privée : rien ne se signe), empreinte calculée, API et
    /// WEB suivis depuis le domaine Commerce. Les systèmes du catalogue reçoivent leur source.
    /// </summary>
    public static (Liaison Liaison, List<EchangeLiaison> Echanges) Liaison(List<SystemeCatalogue> systemes, DateTimeOffset maintenant)
    {
        JwkPublique Signature()
        {
            using var c = CryptoLiaison.NouvelleCleSignature();
            return CryptoLiaison.Jwk(c);
        }
        JwkPublique Chiffrement()
        {
            using var c = CryptoLiaison.NouvelleCleChiffrement();
            return CryptoLiaison.Jwk(c);
        }
        var cles = new ClesLocales { Signature = Signature(), Chiffrement = Chiffrement() };
        var pair = new ClesPair { Signature = Signature(), Chiffrement = Chiffrement() };
        var catalogue = CatalogueNoosphere() with { GenereLe = SynchroLiaison.DateContrat(maintenant.AddMinutes(-12)) };
        var l = new Liaison
        {
            Id = Guid.NewGuid(), NomInstance = catalogue.NomInstance!, Url = UrlNoosphereDemo, VersionPair = "1.5.0", Etat = EtatLiaison.Active,
            ConfirmeeIci = true, ConfirmeeLe = maintenant.AddDays(-6), ConfirmeePar = "Alice", ConfirmeeParPair = true,
            Capacites = [.. CapacitesLiaison.Toutes], Cles = cles, ClesPair = pair, KidPair = CryptoLiaison.Kid(pair.Signature),
            Empreinte = CryptoLiaison.EmpreinteMots([cles.Signature, cles.Chiffrement, pair.Signature, pair.Chiffrement]),
            AppaireeLe = maintenant.AddDays(-6).AddMinutes(-3), AppaireePar = "Alice", DernierEchangeLe = maintenant.AddMinutes(-12),
            DomainesSuivis = ["commerce"], EtagCatalogue = "\"demo-1\"", Catalogue = catalogue, DerniereSynchro = maintenant.AddMinutes(-12),
        };
        var bilan = new BilanSynchro();
        foreach (var (app, systeme) in new[] { ("api-commandes", "API"), ("portail-web", "WEB") })
            if (systemes.FirstOrDefault(s => s.Nom == systeme) is { } s)
                SynchroLiaison.Suivre(l, catalogue, catalogue.Applications!.First(a => a.Id == app), systemes, s.Id, maintenant.AddMinutes(-12), bilan);
        l.ApplicationsConnues = [.. catalogue.Applications!.Select(a => a.Id).Order(StringComparer.Ordinal)];
        EchangeLiaison E(int minutes, string sens, string op, string resultat, int? code, string detail) => new()
        {
            LiaisonId = l.Id, Quand = maintenant.AddMinutes(-minutes), Sens = sens, Operation = op, Resultat = resultat, Code = code, Detail = detail,
        };
        return (l,
        [
            E(6 * 24 * 60 + 3, "sortant", "pair", "ok", 200, $"Appairée avec {l.NomInstance} ({UrlNoosphereDemo}) ; empreinte {l.Empreinte}, à confirmer."),
            E(6 * 24 * 60, "entrant", "status", "ok", 200, "État demandé par Noosphere."),
            E(2 * 24 * 60, "entrant", "notices", "ok", 202, "1 changement(s) : application api-commandes upsert ; catalogue relu dans quelques secondes."),
            E(95, "entrant", "status", "refuse", 401, "requête rejouée (nonce déjà vu) (replay)"),
            E(12, "sortant", "catalog", "demo", null, "Démonstration : catalogue fictif (6 applications)."),
            E(11, "sortant", "deployments", "demo", null, "Démonstration : événements non envoyés."),
        ]);
    }
}
