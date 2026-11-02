using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionMep.Api.Donnees;
using GestionMep.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMep.Tests;

/// <summary>
/// Calendrier des MEP et périodes de gel par l'API (#129) : administration des périodes (journal), Go refusé pendant un gel
/// (problème typé) puis accordé par dérogation, calendrier filtré et paginé avec ses conflits, export et abonnement iCalendar,
/// variable {{gel}}, calcul du créneau des MEP existantes.
/// </summary>
[Collection("Api")]
public class ApiCalendrierTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static Task<JsonElement> Ok(HttpResponseMessage rep) => AidesApi.Ok(rep);

    private static async Task<JsonElement> Probleme(HttpResponseMessage rep, HttpStatusCode attendu)
    {
        var texte = await rep.Content.ReadAsStringAsync();
        Assert.True(rep.StatusCode == attendu, $"{(int)rep.StatusCode} au lieu de {(int)attendu} : {texte}");
        Assert.Equal("application/problem+json", rep.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(texte).RootElement.Clone();
    }

    private static async Task<List<string>> TypesJournal(HttpClient c, Guid id) =>
        [.. (await Ok(await c.GetAsync($"/api/meps/{id}/journal"))).EnumerateArray().Select(e => e.GetProperty("type").GetString()!)];

    private static async Task<List<(string Type, string Detail)>> JournalAdmin(HttpClient admin) =>
        [.. (await Ok(await admin.GetAsync("/api/admin/journal"))).EnumerateArray().Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("detail").GetString()!))];

    private static async Task<Guid> Systeme(HttpClient c, string nom) =>
        (await Ok(await c.GetAsync("/api/catalogue/systemes"))).EnumerateArray().Single(s => s.GetProperty("nom").GetString() == nom).GetProperty("id").GetGuid();

    private static async Task<JsonElement> CreerGel(HttpClient admin, object saisie) =>
        await Ok(await admin.PostAsJsonAsync("/api/admin/periodes-gel", saisie));

    /// <summary>MEP en préparation créée en base, annoncée le jour donné de <paramref name="debut"/> à <paramref name="fin"/> (Paris), sur ce système du catalogue.</summary>
    private async Task<Guid> MepAnnoncee(string outil, DateOnly jour, TimeOnly debut, TimeOnly fin, Guid systeme, string nomSysteme)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var m = new Mep { Outil = outil, VersionCible = "1.0", CreeLe = DateTimeOffset.UtcNow };
        m.Communication.DateAnnoncee = jour;
        m.Communication.HeureDebutAnnoncee = debut;
        m.Communication.HeureFinAnnoncee = fin;
        m.Systemes.Add(new Systeme { MepId = m.Id, Nom = nomSysteme, CatalogueId = systeme, Environnements = [new EnvironnementSysteme { Nom = "Production", Production = true }] });
        db.Meps.Add(m);
        await db.SaveChangesAsync();
        return m.Id;
    }

    private static DateOnly JourDans(int jours) => Fuseaux.Jour(DateTimeOffset.UtcNow, Fuseaux.ParDefaut).AddDays(jours);

    [Fact]
    public async Task Un_administrateur_gere_les_periodes_de_gel_et_chaque_changement_est_au_journal()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var bdd = await Systeme(alice, "BDD");
        var saisie = new
        {
            nom = "Clôture comptable", debut = "2026-11-28T00:00:00+01:00", fin = "2026-12-01T00:00:00+01:00", motif = "Arrêté des comptes",
            systemeCatalogueIds = new[] { bdd }, recurrence = "FinDeMois", productionSeulement = true,
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/admin/periodes-gel", saisie)).StatusCode);
        var creee = await Ok(await alice.PostAsJsonAsync("/api/admin/periodes-gel", saisie));
        var id = creee.GetProperty("id").GetGuid();
        Assert.Equal("2026-11-27T23:00:00+00:00", creee.GetProperty("debut").GetDateTimeOffset().ToString("yyyy-MM-dd'T'HH:mm:sszzz"));
        Assert.Equal(["BDD"], creee.GetProperty("systemes").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal("Europe/Paris", creee.GetProperty("fuseauHoraire").GetString());

        // Lecture pour tous ; modification avec la version lue (409 si périmée) ; saisie invalide : 400 avec chaque motif.
        Assert.Contains((await Ok(await bob.GetAsync("/api/periodes-gel"))).EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);
        var modifiee = await Ok(await alice.PutAsJsonAsync($"/api/admin/periodes-gel/{id}", new
        {
            saisie.nom, saisie.debut, fin = "2026-12-02T08:00:00+01:00", saisie.motif, saisie.systemeCatalogueIds, saisie.recurrence, version = 0,
        }));
        Assert.Equal(1, modifiee.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PutAsJsonAsync($"/api/admin/periodes-gel/{id}", new { saisie.nom, saisie.debut, saisie.fin, version = 0 })).StatusCode);
        var invalide = await Probleme(await alice.PostAsJsonAsync("/api/admin/periodes-gel",
            new { nom = "", debut = "2026-12-10T00:00:00Z", fin = "2026-12-01T00:00:00Z" }), HttpStatusCode.BadRequest);
        Assert.Equal(2, invalide.GetProperty("erreurs").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/admin/periodes-gel/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/admin/periodes-gel/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/admin/periodes-gel/{id}")).StatusCode);

        var journal = await JournalAdmin(alice);
        Assert.Contains(journal, e => e.Type == "gel.cree" && e.Detail.Contains("« Clôture comptable »") && e.Detail.Contains("systèmes BDD")
            && e.Detail.Contains("production seulement") && e.Detail.Contains("Arrêté des comptes"));
        Assert.Contains(journal, e => e.Type == "gel.modifie" && e.Detail.Contains('→'));
        Assert.Contains(journal, e => e.Type == "gel.supprime");
    }

    [Fact]
    public async Task Le_go_pendant_un_gel_renvoie_un_probleme_clair_puis_passe_par_derogation_accordee_par_l_approbateur()
    {
        var alice = await _api.ClientConnecte("alice");
        var claire = await _api.ClientConnecte("claire");
        var id = await AidesApi.IdDemo(alice);
        var maintenant = DateTimeOffset.UtcNow;
        var gel = await CreerGel(alice, new { nom = "Fêtes de fin d'année", debut = maintenant.AddHours(-1), fin = maintenant.AddHours(6), motif = "Activité commerciale" });
        var gelId = gel.GetProperty("id").GetGuid();

        await AidesApi.Approuver(_api, id);
        var criteres = await AidesApi.CriteresActifs(alice);
        var refus = await Probleme(await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", criteresCoches = criteres }),
            HttpStatusCode.UnprocessableEntity);
        Assert.Equal("urn:gestion-mep:probleme:go-pendant-un-gel", refus.GetProperty("type").GetString());
        Assert.Equal("Règle métier", refus.GetProperty("title").GetString());
        Assert.Contains("« Fêtes de fin d'année »", refus.GetProperty("detail").GetString());
        Assert.Equal(gelId, refus.GetProperty("gels")[0].GetProperty("periodeGelId").GetGuid());

        // Signalé sur la MEP : le Go toucherait ce gel sans dérogation.
        var mep = await Ok(await alice.GetAsync($"/api/meps/{id}"));
        Assert.Equal(gelId, mep.GetProperty("calendrier").GetProperty("gelsAuGo")[0].GetProperty("periodeGelId").GetGuid());
        Assert.Equal("Preparation", mep.GetProperty("phase").GetString());

        // Demande : motif obligatoire, pas pour Claire (approbatrice) ; décision : jamais par le pilote.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/derogations-gel", new { periodeGelId = gelId, motif = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.PostAsJsonAsync($"/api/meps/{id}/derogations-gel", new { periodeGelId = gelId, motif = "Urgent" })).StatusCode);
        var demandee = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/derogations-gel", new { periodeGelId = gelId, motif = "Correctif de sécurité" }));
        var derogation = demandee.GetProperty("calendrier").GetProperty("derogations")[0];
        Assert.Equal("Demandee", derogation.GetProperty("statut").GetString());
        Assert.False(derogation.GetProperty("peutDecider").GetBoolean());
        var derogationId = derogation.GetProperty("id").GetGuid();
        Assert.True((await Ok(await claire.GetAsync($"/api/meps/{id}"))).GetProperty("calendrier").GetProperty("derogations")[0].GetProperty("peutDecider").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsJsonAsync($"/api/meps/{id}/derogations-gel/{derogationId}/decision", new { accorder = true })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await claire.PostAsJsonAsync($"/api/meps/{id}/derogations-gel/{derogationId}/decision", new { accorder = false })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", criteresCoches = criteres })).StatusCode);
        var accordee = await Ok(await claire.PostAsJsonAsync($"/api/meps/{id}/derogations-gel/{derogationId}/decision", new { accorder = true, commentaire = "Risque accepté" }));
        Assert.Equal("Accordee", accordee.GetProperty("calendrier").GetProperty("derogations")[0].GetProperty("statut").GetString());
        Assert.Empty(accordee.GetProperty("calendrier").GetProperty("gelsAuGo").EnumerateArray());

        var go = await Ok(await alice.PostAsJsonAsync($"/api/meps/{id}/phase", new { phase = "Execution", criteresCoches = criteres }));
        Assert.Equal("Execution", go.GetProperty("phase").GetString());
        var journal = await TypesJournal(alice, id);
        Assert.Contains("gel.derogation.demandee", journal);
        Assert.Contains("gel.derogation.accordee", journal);
        Assert.Contains("gel.go", journal);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await alice.PostAsJsonAsync($"/api/meps/{id}/derogations-gel", new { periodeGelId = gelId, motif = "Après le Go" })).StatusCode);
    }

    [Fact]
    public async Task Le_calendrier_filtre_pagine_signale_les_conflits_et_les_gels_et_le_detail_d_une_mep_aussi()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var bdd = await Systeme(alice, "BDD");
        var web = await Systeme(alice, "WEB");
        var jour = JourDans(40);
        var a = await MepAnnoncee("Calendrier A", jour, new TimeOnly(20, 0), new TimeOnly(22, 0), bdd, "BDD");
        var b = await MepAnnoncee("Calendrier B", jour, new TimeOnly(21, 0), new TimeOnly(23, 0), bdd, "BDD");
        var c = await MepAnnoncee("Calendrier C", jour, new TimeOnly(21, 0), new TimeOnly(23, 0), web, "WEB");
        await CreerGel(alice, new
        {
            nom = "Inventaire", debut = Fuseaux.Instant(jour, new TimeOnly(18, 0), "Europe/Paris"), fin = Fuseaux.Instant(jour.AddDays(1), null, "Europe/Paris"),
            systemeCatalogueIds = new[] { bdd },
        });

        var du = jour.ToString("yyyy-MM-dd");
        var page = await Ok(await bob.GetAsync($"/api/calendrier?du={du}&au={du}"));
        var meps = page.GetProperty("meps").EnumerateArray().ToList();
        Assert.Equal([a, b, c], meps.Where(m => m.GetProperty("outil").GetString()!.StartsWith("Calendrier", StringComparison.Ordinal)).Select(m => m.GetProperty("id").GetGuid()));
        var ma = meps.Single(m => m.GetProperty("id").GetGuid() == a);
        Assert.Equal(Fuseaux.Instant(jour, new TimeOnly(20, 0), "Europe/Paris"), ma.GetProperty("debut").GetDateTimeOffset());
        Assert.True(ma.GetProperty("prevu").GetBoolean());
        Assert.True(ma.GetProperty("toucheProduction").GetBoolean());
        Assert.Equal([b], ma.GetProperty("conflits").EnumerateArray().Select(x => x.GetProperty("mepId").GetGuid()));
        Assert.Equal(["BDD"], ma.GetProperty("conflits")[0].GetProperty("systemes").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("Inventaire", ma.GetProperty("gels")[0].GetProperty("nom").GetString());
        var mc = meps.Single(m => m.GetProperty("id").GetGuid() == c);
        Assert.Empty(mc.GetProperty("conflits").EnumerateArray());
        Assert.Empty(mc.GetProperty("gels").EnumerateArray());
        Assert.Contains(page.GetProperty("gels").EnumerateArray(), g => g.GetProperty("nom").GetString() == "Inventaire");

        // Filtre par système, pages par curseur.
        var filtree = await Ok(await bob.GetAsync($"/api/calendrier?du={du}&au={du}&systeme={web}"));
        Assert.Equal([c], filtree.GetProperty("meps").EnumerateArray().Select(m => m.GetProperty("id").GetGuid()));
        var p1 = await Ok(await bob.GetAsync($"/api/calendrier?du={du}&au={du}&systeme={bdd}&limite=1"));
        Assert.Equal(2, p1.GetProperty("total").GetInt32());
        var p2 = await Ok(await bob.GetAsync($"/api/calendrier?du={du}&au={du}&systeme={bdd}&limite=1&apres={p1.GetProperty("suivant").GetString()}"));
        Assert.Equal(b, p2.GetProperty("meps")[0].GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, p2.GetProperty("suivant").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.GetAsync($"/api/calendrier?du=2026-01-01&au=2027-06-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.GetAsync($"/api/calendrier?du={du}&fuseau=Mars/Olympus")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.GetAsync($"/api/calendrier?du={du}&limite=501")).StatusCode);

        // Détail de la MEP : même créneau, même conflit, même gel (signalé, jamais bloquant pour la modification).
        var detail = (await Ok(await bob.GetAsync($"/api/meps/{b}"))).GetProperty("calendrier");
        Assert.Equal([a], detail.GetProperty("conflits").EnumerateArray().Select(x => x.GetProperty("mepId").GetGuid()));
        Assert.Equal("Inventaire", detail.GetProperty("gels")[0].GetProperty("nom").GetString());
        Assert.False(detail.GetProperty("journeeEntiere").GetBoolean());

        // {{gel}} dans les modèles de texte.
        var variables = await Ok(await bob.GetAsync($"/api/meps/{a}/variables-texte"));
        var gel = variables.GetProperty("valeurs").EnumerateArray().Single(v => v.GetProperty("cle").GetString() == "gel");
        Assert.StartsWith("« Inventaire » du ", gel.GetProperty("texte").GetString());
        Assert.EndsWith(": sans dérogation", gel.GetProperty("texte").GetString());
    }

    [Fact]
    public async Task L_export_ics_se_relit_et_l_abonnement_par_lien_secret_se_regenere_et_se_revoque()
    {
        var alice = await _api.ClientConnecte("alice");
        var bob = await _api.ClientConnecte("bob");
        var bdd = await Systeme(alice, "BDD");
        var jour = JourDans(20);
        var a = await MepAnnoncee("Export ; virgule, et accents", jour, new TimeOnly(20, 0), new TimeOnly(22, 0), bdd, "BDD");
        await CreerGel(alice, new { nom = "Gel de test", debut = Fuseaux.Instant(jour, null, "Europe/Paris"), fin = Fuseaux.Instant(jour.AddDays(1), null, "Europe/Paris") });

        var du = jour.ToString("yyyy-MM-dd");
        var rep = await bob.GetAsync($"/api/calendrier/ics?du={du}&au={du}");
        Assert.Equal(HttpStatusCode.OK, rep.StatusCode);
        Assert.Equal("text/calendar", rep.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".ics", rep.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var evenements = ICalendarTests.Verifier(await rep.Content.ReadAsStringAsync());
        var e = evenements.Single(x => x.V("UID") == $"mep-{a}@gestion-mep");
        Assert.Equal("[Production] MEP Export ; virgule, et accents 1.0", ICalendarTests.Texte(e.V("SUMMARY")));
        Assert.Equal(Fuseaux.Instant(jour, new TimeOnly(20, 0), "Europe/Paris"), ICalendarTests.DateUtc(e.V("DTSTART")));
        Assert.Equal("TENTATIVE", e.V("STATUS"));
        Assert.Contains("Période de gel « Gel de test »", ICalendarTests.Texte(e.V("DESCRIPTION")));
        Assert.Contains(evenements, x => ICalendarTests.Texte(x.V("SUMMARY")) == "Gel : Gel de test" && x.V("TRANSP") == "TRANSPARENT");
        // Même UID d'un export à l'autre ; sans les gels sur demande.
        var sansGels = ICalendarTests.Verifier(await (await bob.GetAsync($"/api/calendrier/ics?du={du}&au={du}&gels=0")).Content.ReadAsStringAsync());
        Assert.Contains(sansGels, x => x.V("UID") == $"mep-{a}@gestion-mep");
        Assert.DoesNotContain(sansGels, x => x.V("UID").StartsWith("gel-", StringComparison.Ordinal));

        // Abonnement : l'adresse n'est donnée qu'à la création, se lit sans session, cesse de répondre une fois régénérée ou révoquée.
        Assert.False((await Ok(await bob.GetAsync("/api/calendrier/abonnement"))).GetProperty("actif").GetBoolean());
        var url = (await Ok(await bob.PostAsync("/api/calendrier/abonnement", null))).GetProperty("url").GetString()!;
        var chemin = new Uri(url).PathAndQuery;
        Assert.Matches("^/api/calendrier/abonnement/[A-Za-z0-9_-]{43}\\.ics$", chemin);
        var anonyme = _api.ClientSansEnTete();
        var flux = await anonyme.GetAsync(chemin);
        Assert.Equal(HttpStatusCode.OK, flux.StatusCode);
        Assert.Contains(ICalendarTests.Verifier(await flux.Content.ReadAsStringAsync()), x => x.V("UID") == $"mep-{a}@gestion-mep");
        var filtre = ICalendarTests.Verifier(await (await anonyme.GetAsync(chemin + "?systeme=" + await Systeme(alice, "WEB"))).Content.ReadAsStringAsync());
        Assert.DoesNotContain(filtre, x => x.V("UID") == $"mep-{a}@gestion-mep");
        var etat = await Ok(await bob.GetAsync("/api/calendrier/abonnement"));
        Assert.True(etat.GetProperty("actif").GetBoolean());
        Assert.Equal(JsonValueKind.Null, etat.GetProperty("url").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, etat.GetProperty("dernierAcces").ValueKind);

        var nouvelle = new Uri((await Ok(await bob.PostAsync("/api/calendrier/abonnement", null))).GetProperty("url").GetString()!).PathAndQuery;
        Assert.NotEqual(chemin, nouvelle);
        Assert.Equal(HttpStatusCode.NotFound, (await anonyme.GetAsync(chemin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonyme.GetAsync(nouvelle)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonyme.GetAsync("/api/calendrier/abonnement/inconnu.ics")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.DeleteAsync("/api/calendrier/abonnement")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonyme.GetAsync(nouvelle)).StatusCode);
        Assert.Contains(await JournalAdmin(alice), x => x.Type == "calendrier.abonnement" && x.Detail.Contains("révoqué"));
        // Sans session, la gestion de l'abonnement est refusée.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonyme.GetAsync("/api/calendrier/abonnement")).StatusCode);
    }

    [Fact]
    public async Task Le_creneau_des_mep_existantes_est_calcule_au_demarrage_apres_la_migration()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MepDbContext>();
        var attendus = await db.Meps.AsNoTracking().ToDictionaryAsync(m => m.Id, m => (m.Calendrier.Debut, m.Calendrier.Fin, m.Calendrier.JourneeEntiere));
        Assert.Contains(attendus.Values, f => f.Debut is not null);
        // État laissé par la migration : colonnes vides, format 0.
        await db.Database.ExecuteSqlRawAsync("""UPDATE "Meps" SET "Calendrier_Debut" = NULL, "Calendrier_Fin" = NULL, "Calendrier_JourneeEntiere" = false, "Calendrier_Format" = 0""");
        Assert.Equal(attendus.Count, await Amorcage.RecalculerCalendrier(db));
        db.ChangeTracker.Clear();
        var recalcules = await db.Meps.AsNoTracking().ToDictionaryAsync(m => m.Id, m => (m.Calendrier.Debut, m.Calendrier.Fin, m.Calendrier.JourneeEntiere));
        Assert.Equal(attendus, recalcules);
        Assert.Equal(0, await Amorcage.RecalculerCalendrier(db));
    }
}
