using System.Text.Json;
using System.Text.Json.Serialization;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MepDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Mep")
        ?? throw new InvalidOperationException("Chaîne de connexion ConnectionStrings:Mep absente (variable d'environnement ConnectionStrings__Mep).")));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
// Un corps JSON invalide lève BadHttpRequestException, traduite en 400 JSON par le middleware d'erreurs.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DiffuseurMep>();
builder.Services.AddScoped<ServiceMep>();
// Pièces jointes : dossier Stockage:Dossier, taille maximale Stockage:TailleMaxMo (le formulaire accepte 1 Mo de marge).
var stockage = new StockagePiecesJointes(builder.Configuration);
builder.Services.AddSingleton(stockage);
var limiteCorps = stockage.TailleMaxOctets + 1024 * 1024;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = limiteCorps);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = limiteCorps);
// Surveillance des échéances de retour arrière (désactivable : Surveillance:Activer=false, utile en test).
if (builder.Configuration.GetValue("Surveillance:Activer", true))
    builder.Services.AddHostedService<SurveillanceEcheances>();
builder.Services.AjouterAuthentification();

var app = builder.Build();

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
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/sante", () => Results.Ok(new { statut = "ok" })).AllowAnonymous();
// Version du front servi (empreinte de index.html) : un onglet ouvert la compare pour proposer de recharger après un déploiement.
var versionFront = VersionFront(app.Environment);
api.MapGet("/version", () => Results.Ok(new { version = versionFront })).AllowAnonymous();
api.MapAuth();
api.MapMeps();
api.MapStructure();
api.MapEtapes();
api.MapPhase();
api.MapCatalogue();
api.MapResolutions();
api.MapDeroule();
api.MapParametres();
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
