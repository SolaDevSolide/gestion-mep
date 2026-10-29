using System.Text.Json;

namespace GestionMep.Api.Cli;

/// <summary>
/// Socle des commandes « connexions » (LigneDeCommande.Connexions.cs) : liste de commandes décrites (usage, options,
/// exemples), sorties (messages, avertissements, résultat de l'enveloppe JSON <c>{ code, messages, avertissements,
/// resultat }</c>), confirmation par <c>--oui</c> et codes de retour (0 succès, 1 échec, 2 usage, 4 confirmation, 5 introuvable).
/// Mêmes noms et même forme que le registre des commandes de la ligne de commande complète (#113) : une fois celui-ci en
/// place, ce fichier disparaît et <see cref="CommandesConnexions"/> rejoint le registre.
/// </summary>
public static partial class LigneDeCommande
{
    public static class Codes
    {
        public const int Succes = 0;
        public const int Echec = 1;
        public const int Usage = 2;
        public const int Environnement = 3;
        public const int Confirmation = 4;
        public const int Introuvable = 5;
    }

    private sealed record Commande(string Nom, string Usage, string Resume, Func<Appel, Task<int>> Executer)
    {
        public (string Option, string Description)[] Options { get; init; } = [];
        public string[] Exemples { get; init; } = [];
        public string? Details { get; init; }
        public string? Json { get; init; }
        public bool Destructive { get; init; }
        public string Verbe => Nom.Contains(' ') ? Nom[(Nom.IndexOf(' ') + 1)..] : "";
    }

    private sealed record GroupeCommandes(string Nom, string Titre, string? Note = null);

    private sealed class Appel(string[] args)
    {
        public string[] Args => args;
        private static bool EstOption(string a) => a.Length > 1 && a[0] == '-' && !char.IsDigit(a[1]);
        public string Arg(int i, string nom) =>
            i < args.Length && !EstOption(args[i]) ? args[i] : throw new ErreurUsage($"{nom} attendu{(i < args.Length ? $" avant « {args[i]} »" : "")}");
        public string? ArgFacultatif(int i) => i < args.Length && !EstOption(args[i]) ? args[i] : null;
        public string[] Depuis(int i) => i < args.Length ? args[i..] : [];
        public void Rien(int n)
        {
            if (args.Length > n) throw new ErreurUsage(EstOption(args[n]) ? $"option inattendue « {args[n]} »" : $"argument inattendu « {args[n]} »");
        }
    }

    private sealed class ConfirmationRequise(string message) : Exception(message);

    private static readonly (string, string) OptionOui = ("--oui", "Confirme l'opération sans question (obligatoire hors terminal)");

    private sealed class SortiesConnexions
    {
        public readonly List<string> Messages = [];
        public readonly List<string> Avertissements = [];
        public object? Resultat;
        public bool Enveloppe;
    }

    private static readonly AsyncLocal<SortiesConnexions?> Sorties = new();

    private static void Ecrire(string ligne = "")
    {
        if (EnJson) Sorties.Value?.Messages.Add(ligne);
        else Console.WriteLine(ligne);
    }

    private static void Informer(string ligne)
    {
        if (EnJson && Sorties.Value is { } s)
        {
            s.Messages.Add(ligne);
            s.Enveloppe = true;
        }
        else if (!EnJson) Console.WriteLine(ligne);
    }

    private static void Avertir(string ligne)
    {
        if (EnJson && Sorties.Value is { } s)
        {
            s.Avertissements.Add(ligne);
            s.Enveloppe = true;
        }
        Console.Error.WriteLine(ligne);
    }

    private static void Resultat(object? valeur)
    {
        if (Sorties.Value is not { } s) return;
        s.Resultat = valeur;
        s.Enveloppe = true;
    }

    private static void Confirmer(bool oui, IEnumerable<string> explication, string commandeAvecOui)
    {
        if (oui) return;
        foreach (var ligne in explication) Console.Error.WriteLine(ligne);
        if (EnJson || Console.IsInputRedirected)
            throw new ConfirmationRequise($"Confirmation requise : relancer avec --oui ({commandeAvecOui}). Rien n'a été modifié.");
        Console.Error.Write("Confirmer ? Taper « oui » pour continuer : ");
        if (Console.ReadLine()?.Trim().ToLowerInvariant() is not ("oui" or "o")) throw new ConfirmationRequise("Abandon : rien n'a été modifié.");
    }

    /// <summary>« gestion-mep connexions … » : aide du groupe, exécution et enveloppe JSON, codes de retour.</summary>
    private static async Task<int> Connexions(string[] args)
    {
        if (args is [] or ["aide" or "--help" or "-h"])
        {
            AideConnexions(args.Length == 0 ? Console.Error : Console.Out);
            return args.Length == 0 ? Codes.Usage : Codes.Succes;
        }
        var commande = CommandesConnexions.FirstOrDefault(c => c.Verbe == args[0])
            ?? throw new ErreurUsage($"action inconnue « connexions {args[0]} » ({string.Join(", ", CommandesConnexions.Select(c => c.Verbe))})");
        if (args.Contains("--help"))
        {
            Console.WriteLine($"gestion-mep {commande.Nom} {commande.Usage}\n  {commande.Resume}");
            foreach (var (o, d) in commande.Options) Console.WriteLine($"    {o,-34} {d}");
            if (commande.Details is { } details) Console.WriteLine($"  {details}");
            foreach (var e in commande.Exemples) Console.WriteLine($"  Exemple : {e}");
            return Codes.Succes;
        }
        var sorties = Sorties.Value = new SortiesConnexions();
        int code;
        try
        {
            code = await commande.Executer(new Appel(args[1..]));
        }
        catch (ConfirmationRequise e)
        {
            Console.Error.WriteLine(e.Message);
            return Codes.Confirmation;
        }
        catch (Services.ErreurHttp e) when (e.Statut == 404)
        {
            Console.Error.WriteLine($"Erreur : {e.Message}");
            return Codes.Introuvable;
        }
        if (EnJson && sorties.Enveloppe)
            Console.WriteLine(JsonSerializer.Serialize(new { code, messages = sorties.Messages, avertissements = sorties.Avertissements, resultat = sorties.Resultat }, OptionsJson));
        return code;
    }

    private static void AideConnexions(TextWriter sortie)
    {
        sortie.WriteLine($"{GroupeConnexions.Titre}\n  {GroupeConnexions.Note}");
        foreach (var c in CommandesConnexions) sortie.WriteLine($"  gestion-mep {c.Nom} {c.Usage}\n      {c.Resume}");
    }
}
