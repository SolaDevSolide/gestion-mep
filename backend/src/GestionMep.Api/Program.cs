using System.Text.Json;
using System.Text.Json.Serialization;
using GestionMep.Api.Auth;
using GestionMep.Api.Cli;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Securite;
using GestionMep.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

// Console Windows : accents lisibles dans les messages des commandes (code page UTF-8 au lieu de 850).
if (OperatingSystem.IsWindows())
{
    try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); } catch (IOException) { }
}

// HEALTHCHECK de l'image Docker : « dotnet GestionMep.Api.dll --sante ».
if (args.Contains("--sante"))
{
    Environment.ExitCode = await ProtectionsHttp.VerifierSante();
    return;
}

// Commandes d'exploitation et d'administration (« gestion-mep aide ») : tout ce que règle la console, plus migration et sauvegardes.
if (LigneDeCommande.EstCommande(args))
{
    Environment.ExitCode = await LigneDeCommande.Executer(args);
    return;
}

// Service Windows : lancé depuis C:\Windows\System32, l'application (wwwroot) est dans le dossier de l'exécutable.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});
// Installation sans Docker : fichier de configuration (mêmes noms que le .env), adresse d'écoute et service système
// (systemd ou service Windows ; sans effet ailleurs).
var fichierInstallation = FichierInstallation.Lire(FichierInstallation.Chemin);
builder.Configuration.AddInMemoryCollection(FichierInstallation.Reglages(fichierInstallation));
if (FichierInstallation.AdresseEcoute(fichierInstallation) is { } ecoute)
    builder.WebHost.UseUrls(ecoute);
builder.Host.UseWindowsService(o => o.ServiceName = "GestionMep");
builder.Host.UseSystemd();
// Réglages modifiés depuis la console ou « gestion-mep reglages » : en base, ils priment sur les variables d'environnement
// et sont relus toutes les 10 secondes (aucun redémarrage).
var sourceReglages = new SourceReglagesBase(builder.Configuration.GetConnectionString("Mep"));
((IConfigurationBuilder)builder.Configuration).Add(sourceReglages);
builder.Services.AddSingleton(sourceReglages.Fournisseur);
builder.Services.AddHostedService<RafraichissementReglages>();

builder.Services.AddDbContext<MepDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Mep")
        ?? throw new InvalidOperationException("Chaîne de connexion ConnectionStrings:Mep absente (variable d'environnement ConnectionStrings__Mep).")));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    // Énumérations en texte uniquement : un entier hors bornes (« decision: 7 ») est refusé au lieu d'être enregistré.
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
});
// Un corps JSON invalide lève BadHttpRequestException, traduite en 400 JSON par le middleware d'erreurs.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DiffuseurMep>();
builder.Services.AddScoped<ServiceMep>();
// Pièces jointes : dossier Stockage:Dossier, taille maximale Stockage:TailleMaxMo (réglage modifiable : le formulaire est
// accepté jusqu'au plafond du réglage, la limite en cours est appliquée à chaque dépôt).
builder.Services.AddSingleton<StockagePiecesJointes>();
builder.Services.AddSingleton<QuotasPiecesJointes>();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = StockagePiecesJointes.LimiteDepotMaximale);
// Corps limité à 1 Mo, sauf sur les routes de dépôt de fichiers (voir PiecesJointesEndpoints.AccepterFichier).
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = LimitesRequetes.CorpsParDefaut);
// Surveillance des échéances de retour arrière (désactivable : Surveillance:Activer=false, utile en test).
if (builder.Configuration.GetValue("Surveillance:Activer", true))
    builder.Services.AddHostedService<SurveillanceEcheances>();
// Indicateur de nouvelle version (MisesAJour:Verifier=false pour ne jamais contacter GitHub).
builder.Services.AddHttpClient(nameof(VerificationMisesAJour), c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<VerificationMisesAJour>();
builder.Services.AddSingleton<InstallationMiseAJour>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<VerificationMisesAJour>());
builder.Services.AjouterAuthentification(builder.Environment);
// Connexion unique facultative (Microsoft Entra ID ou autre OpenID Connect), réglée en base depuis la console ou « gestion-mep sso ».
builder.Services.AjouterConnexionUnique();
builder.Services.AjouterProtectionsHttp(builder.Configuration);
// Clés de chiffrement du cookie de session : conservées dans Cles:Dossier (volume Docker « cles »), sinon chaque
// recréation du conteneur déconnecterait tout le monde. Vide : emplacement par défaut d'ASP.NET Core (poste de dev).
var protection = builder.Services.AddDataProtection().SetApplicationName("gestion-mep");
if (builder.Configuration["Cles:Dossier"] is { Length: > 0 } dossierCles)
    protection.PersistKeysToFileSystem(new DirectoryInfo(dossierCles));
builder.Services.AjouterOpenApi();

var app = builder.Build();

// En premier : adresse et schéma du client derrière le reverse proxy (limite par IP, journaux).
app.UtiliserEnTetesProxy();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UtiliserEnTetesSecurite(app.Environment.WebRootPath is { } racine ? Path.Combine(racine, "index.html") : null);
app.UtiliserGestionErreurs();
app.UseDefaultFiles();
// index.html n'est jamais mis en cache : après un déploiement, le navigateur charge la nouvelle version (les fichiers
// de /assets ont un nom haché et peuvent, eux, rester en cache).
var fichiersStatiques = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    },
};
app.UseStaticFiles(fichiersStatiques);
app.UtiliserProtectionCsrf();
app.UtiliserGardeConnexionUnique();
app.UseAuthentication();
app.UseAuthorization();
app.UtiliserMotDePasseProvisoire();
app.UseRateLimiter();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapOpenApi(DocumentationOpenApi.Chemin);
api.MapGet("/sante", () => Results.Ok(new { statut = "ok" })).AllowAnonymous();
// Version du front servi (empreinte de index.html) : un onglet ouvert la compare pour proposer de recharger après un déploiement.
var versionFront = VersionFront(app.Environment);
api.MapGet("/version", () => Results.Ok(new { version = versionFront })).AllowAnonymous();
api.MapAuth();
api.MapSso();
// Mode démonstration (Demo:Activer) : routes déclarées seulement s'il est actif.
api.MapDemo(app.Configuration);
api.MapApplication();
api.MapMeps();
api.MapStructure();
api.MapObjetsImpactes();
api.MapEtapes();
api.MapSections();
api.MapPhase();
api.MapCatalogue();
api.MapResolutions();
api.MapDeroule();
api.MapRecette();
api.MapExportsExcel();
api.MapEtapesModeles();
api.MapParametres();
api.MapAdmin();
api.MapEquipes();
api.MapCommunication();
api.MapReglages();
api.MapPiecesJointes();
// Une route /api inconnue ne doit jamais retomber sur index.html.
api.Map("{**reste}", () => Results.Json(new { message = "Route introuvable." }, statusCode: 404));

app.MapFallbackToFile("index.html", fichiersStatiques);

await Amorcage.Executer(app.Services);
await app.RunAsync();

static string VersionFront(IWebHostEnvironment env)
{
    var index = env.WebRootPath is null ? null : Path.Combine(env.WebRootPath, "index.html");
    if (index is null || !File.Exists(index)) return "dev";
    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(index)))[..12].ToLowerInvariant();
}

/// <summary>Exposé pour WebApplicationFactory (tests d'intégration).</summary>
public partial class Program;
