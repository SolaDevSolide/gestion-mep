using System.Text.Json;
using System.Text.Json.Serialization;
using GestionMep.Api.Auth;
using GestionMep.Api.Donnees;
using GestionMep.Api.Endpoints;
using GestionMep.Api.Services;
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
builder.Services.AjouterAuthentification();

var app = builder.Build();

app.UtiliserGestionErreurs();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/sante", () => Results.Ok(new { statut = "ok" })).AllowAnonymous();
api.MapAuth();
api.MapMeps();
api.MapStructure();
api.MapEtapes();
api.MapPhase();
// Une route /api inconnue ne doit jamais retomber sur index.html.
api.Map("{**reste}", () => Results.Json(new { message = "Route introuvable." }, statusCode: 404));

app.MapFallbackToFile("index.html");

await Amorcage.Executer(app.Services);
await app.RunAsync();

/// <summary>Exposé pour WebApplicationFactory (tests d'intégration).</summary>
public partial class Program;
