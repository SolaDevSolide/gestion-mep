using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GestionMep.Api.Services;

/// <summary>
/// Description OpenAPI de l'API, servie sur <c>/api/openapi.json</c> (session requise). Une copie est versionnée dans
/// <c>docs/openapi.json</c> : le front en génère ses types (<c>npm run api:types</c>) et un test vérifie qu'elle est à jour.
/// </summary>
public static class DocumentationOpenApi
{
    /// <summary>Chemin du document, relatif au groupe <c>/api</c>.</summary>
    public const string Chemin = "/openapi.json";

    public static IServiceCollection AjouterOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Gestion MEP",
                    Version = "v1",
                    Description = "API de préparation et d'exécution des mises en production. Contrat détaillé : docs/api.md.",
                };
                // Le document décrit l'API, pas un serveur : pas d'adresse propre à l'environnement qui l'a produit.
                document.Servers = [];
                return Task.CompletedTask;
            });
            o.AddDocumentTransformer(AjouterContrats);
            o.AddSchemaTransformer(NombresStricts);
            o.AddSchemaTransformer(ObjetsNullables);
        });

    /// <summary>
    /// Les options JSON « Web » acceptent aussi un nombre écrit en chaîne ("12"), ce que le schéma traduit par
    /// <c>integer | string</c>. Le front envoie et reçoit toujours de vrais nombres : on ne décrit que ceux-là.
    /// </summary>
    private static Task NombresStricts(OpenApiSchema schema, OpenApiSchemaTransformerContext _, CancellationToken __)
    {
        if (schema.Type is { } type && (type & (JsonSchemaType.Integer | JsonSchemaType.Number)) != 0 && type.HasFlag(JsonSchemaType.String))
        {
            schema.Type = type & ~JsonSchemaType.String;
            schema.Pattern = null;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Le générateur décrit une propriété objet nullable (<c>RetourArriereDto? RetourArriere</c>) par une simple référence,
    /// sans le <c>null</c> que le serveur envoie pourtant. On la remplace par « null ou la référence ».
    /// </summary>
    private static Task ObjetsNullables(OpenApiSchema schema, OpenApiSchemaTransformerContext contexte, CancellationToken _)
    {
        if (schema.Properties is null) return Task.CompletedTask;
        foreach (var propriete in contexte.JsonTypeInfo.Properties)
        {
            if (!propriete.IsGetNullable || !schema.Properties.TryGetValue(propriete.Name, out var actuel)) continue;
            switch (actuel)
            {
                case OpenApiSchemaReference reference:
                    schema.Properties[propriete.Name] = new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, reference] };
                    break;
                case OpenApiSchema { Type: { } type } enLigne when type.HasFlag(JsonSchemaType.Object) && !type.HasFlag(JsonSchemaType.Null):
                    // Copie : le même schéma peut décrire ailleurs une propriété non nullable.
                    var copie = (OpenApiSchema)enLigne.CreateShallowCopy();
                    copie.Type = type | JsonSchemaType.Null;
                    schema.Properties[propriete.Name] = copie;
                    break;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ajoute aux composants tous les contrats d'entrée et de sortie (<c>…Dto</c>, <c>…Saisie</c>), même ceux qu'aucune route
    /// ne déclare encore : le front dispose ainsi de tous ses types.
    /// </summary>
    private static async Task AjouterContrats(OpenApiDocument document, OpenApiDocumentTransformerContext contexte, CancellationToken annulation)
    {
        var contrats = typeof(MepDto).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(MepDto).Namespace && t.IsPublic && (t.Name.EndsWith("Dto", StringComparison.Ordinal) || t.Name.EndsWith("Saisie", StringComparison.Ordinal)))
            .OrderBy(t => t.Name, StringComparer.Ordinal);
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        foreach (var type in contrats)
        {
            if (document.Components.Schemas.ContainsKey(type.Name)) continue;
            var schema = await contexte.GetOrCreateSchemaAsync(type, null, annulation);
            document.Components.Schemas[type.Name] = schema;
        }
    }
}
