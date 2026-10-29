using System.Globalization;
using System.Text;
using GestionMep.Api.Services;

namespace GestionMep.Api.Cli;

/// <summary>Aide (globale, d'un groupe, d'une commande), référence Markdown de la documentation et scripts de complétion.</summary>
public static partial class LigneDeCommande
{
    private const string CodesDeRetour =
        "Codes de retour : 0 succès, 1 échec, 2 commande mal formée, 3 base injoignable ou configuration illisible, 4 confirmation requise, 5 introuvable.";

    private static string Ligne(Commande c) => $"gestion-mep {c.Nom}{(c.Usage.Length > 0 ? " " + c.Usage : "")}";

    /// <summary>Aide demandée (« aide », « --help ») : sortie standard, code 0. Groupe ou commande inconnus : code 2.</summary>
    private static int AfficherAide(string[] args)
    {
        if (args is ["--markdown"])
        {
            S.DonneesEcrites = true; // pas d'enveloppe JSON autour de la référence
            Console.Write(ReferenceMarkdown());
            return 0;
        }
        var mots = args.Where(a => !a.StartsWith('-')).Select(NomGroupe).ToArray();
        string texte;
        if (mots.Length == 0) texte = AideGlobale();
        else if (Trouver(string.Join(' ', mots.Take(2))) is { } c && mots.Length <= 2) texte = AideCommande(c);
        else if (mots.Length == 1 && Groupes.Any(g => g.Nom == mots[0])) texte = AideGroupe(mots[0]);
        else if (Trouver(mots[0]) is { } seule && mots.Length == 1) texte = AideCommande(seule);
        else if (Groupes.Any(g => g.Nom == mots[0]))
        {
            var verbes = Registre.Where(x => x.Groupe == mots[0]).Select(x => x.Verbe);
            throw new ErreurUsage($"action inconnue « {mots[0]} {mots[1]} »{Suggestion(mots[1], verbes, mots[0] + " ")}") { Suite = $"Aide du groupe : gestion-mep {mots[0]} --help" };
        }
        else throw new ErreurUsage($"commande inconnue « {mots[0]} »{Suggestion(mots[0], Groupes.Select(g => g.Nom), "")}");
        S.DonneesEcrites = true;
        if (S.Json) EcrireJson(new { Aide = texte });
        else Console.WriteLine(texte);
        return 0;
    }

    private static string AideGlobale()
    {
        var t = new StringBuilder();
        t.AppendLine("gestion-mep : commandes d'exploitation et d'administration de Gestion MEP");
        t.AppendLine();
        t.AppendLine("Usage : gestion-mep <groupe> <action> [arguments] [options]");
        t.AppendLine("        gestion-mep <groupe> <action> --help      aide détaillée de la commande, avec des exemples");
        foreach (var g in Groupes)
        {
            t.AppendLine();
            t.AppendLine(g.Titre);
            foreach (var c in Registre.Where(c => c.Groupe == g.Nom))
            {
                var debut = "  " + Ligne(c);
                t.AppendLine(debut.Length <= 54 ? debut.PadRight(56) + c.Resume : debut + Environment.NewLine + new string(' ', 56) + c.Resume);
            }
        }
        t.AppendLine();
        t.AppendLine("Options communes à toutes les commandes :");
        foreach (var (option, description) in DescriptionOptionsGlobales) t.AppendLine(CultureInfo.InvariantCulture, $"  {option,-20}{description}");
        t.AppendLine("Anciens noms acceptés : sauvegarder (sauvegardes creer), importer (sauvegardes importer), journal (journal lister), reglage (reglages).");
        t.AppendLine(CodesDeRetour);
        t.AppendLine();
        t.Append(AideInstallation());
        return t.ToString().TrimEnd();
    }

    private static string AideGroupe(string groupe)
    {
        var g = Groupes.First(x => x.Nom == groupe);
        var t = new StringBuilder();
        t.AppendLine(g.Titre + (g.Note is null ? "" : $" — {g.Note}"));
        foreach (var c in Registre.Where(c => c.Groupe == groupe))
        {
            t.AppendLine();
            t.AppendLine("  " + Ligne(c));
            t.AppendLine("      " + c.Resume);
        }
        t.AppendLine();
        t.AppendLine(CultureInfo.InvariantCulture, $"Aide détaillée et exemples : gestion-mep {groupe} <action> --help");
        return t.ToString().TrimEnd();
    }

    private static string AideCommande(Commande c)
    {
        var t = new StringBuilder();
        t.AppendLine(CultureInfo.InvariantCulture, $"Usage : {Ligne(c)}");
        t.AppendLine();
        t.AppendLine(c.Resume + ".");
        if (Groupes.FirstOrDefault(g => g.Nom == c.Groupe) is { Note: { } note } && c.Verbe.Length > 0) t.AppendLine(note);
        if (c.Details is { } d) t.AppendLine(d);
        if (c.Options.Length > 0)
        {
            t.AppendLine();
            t.AppendLine("Options :");
            var largeur = Math.Min(34, c.Options.Max(o => o.Option.Length) + 2);
            foreach (var (option, description) in c.Options)
                t.AppendLine(option.Length + 2 > largeur ? $"  {option}{Environment.NewLine}  {new string(' ', largeur)}{description}" : $"  {option.PadRight(largeur)}{description}");
        }
        t.AppendLine("Options communes : --json, --quiet (-q), --verbose (-v), --non-interactif, --config <fichier>, --help (-h).");
        t.AppendLine();
        t.AppendLine(CultureInfo.InvariantCulture, $"Sortie --json : {SortieJson(c)}.");
        if (c.Exemples.Length > 0)
        {
            t.AppendLine();
            t.AppendLine("Exemples :");
            foreach (var e in c.Exemples) t.AppendLine("  " + e);
        }
        if (c.Alias.Length > 0)
        {
            t.AppendLine();
            t.AppendLine(CultureInfo.InvariantCulture, $"Autre nom : {string.Join(", ", c.Alias.Select(a => "gestion-mep " + a))}");
        }
        t.AppendLine();
        t.Append(CodesDeRetour);
        return t.ToString();
    }

    private static string SortieJson(Commande c) => c.Json switch
    {
        null => "enveloppe { code, messages, avertissements, resultat }",
        var j when j.StartsWith("resultat", StringComparison.Ordinal) => $"enveloppe {{ code, messages, avertissements, resultat }} ; {j}",
        var j => j,
    };

    /// <summary>Fin de l'aide propre au type d'installation : comment lancer une commande, restaurer, passer un fichier.</summary>
    private static string AideInstallation() => Installation.Mode switch
    {
        Installation.Docker => """
            Dans Docker : docker compose exec app gestion-mep <commande>
            Restaurer (application arrêtée) : docker compose stop app
                                              docker compose run --rm app sauvegardes restaurer <archive> --oui
                                              docker compose up -d app
            Secret depuis un fichier : docker compose exec -T app gestion-mep sso configurer --secret-stdin < secret.txt
            Fichier depuis l'hôte : docker compose exec -T app gestion-mep resolutions importer - < resolutions.json
            Charte depuis l'hôte : docker compose exec -T app gestion-mep charte importer - < charte.json
            Scripts : -T (sans terminal) ; une opération destructive y demande --oui.
            """,
        Installation.Linux => $"""
            Installation sans Docker (Linux) : sudo gestion-mep <commande> ; configuration : {FichierInstallation.Chemin}
            Restaurer (application arrêtée) : sudo systemctl stop gestion-mep
                                              sudo gestion-mep sauvegardes restaurer <archive> --oui
                                              sudo systemctl start gestion-mep
            Secret depuis un fichier : sudo gestion-mep sso configurer --secret-stdin < secret.txt
            Complétion : sudo gestion-mep completion bash > /etc/bash_completion.d/gestion-mep (posée par le script d'installation)
            """,
        _ => $"""
            Installation sans Docker (Windows, PowerShell administrateur) : gestion-mep <commande> ; configuration : {FichierInstallation.Chemin}
            Restaurer (application arrêtée) : Stop-Service GestionMep
                                              gestion-mep sauvegardes restaurer <archive> --oui
                                              Start-Service GestionMep
            Secret depuis un fichier : Get-Content secret.txt | gestion-mep sso configurer --secret-stdin
            Complétion : gestion-mep completion powershell | Out-String | Invoke-Expression (à ajouter à $PROFILE)
            """,
    };

    // ---------- Référence Markdown (docs/Ligne-de-commande.md) ----------

    /// <summary>Référence complète, identique quel que soit le type d'installation (docs/Ligne-de-commande.md la reprend telle quelle).</summary>
    internal static string ReferenceMarkdown()
    {
        static string Cellule(string s) => s.Replace("|", "\\|");
        var t = new StringBuilder();
        foreach (var g in Groupes)
        {
            t.AppendLine(CultureInfo.InvariantCulture, $"### {g.Titre}");
            t.AppendLine();
            if (g.Note is { } note)
            {
                t.AppendLine(note);
                t.AppendLine();
            }
            foreach (var c in Registre.Where(c => c.Groupe == g.Nom))
            {
                t.AppendLine(CultureInfo.InvariantCulture, $"#### `{c.Nom}`");
                t.AppendLine();
                t.AppendLine("```");
                t.AppendLine(Ligne(c));
                t.AppendLine("```");
                t.AppendLine();
                t.AppendLine(c.Resume + "." + (c.Details is { } d ? " " + d : ""));
                t.AppendLine();
                if (c.Options.Length > 0)
                {
                    t.AppendLine("| Option | Effet |");
                    t.AppendLine("|---|---|");
                    foreach (var (option, description) in c.Options) t.AppendLine(CultureInfo.InvariantCulture, $"| `{Cellule(option)}` | {Cellule(description)} |");
                    t.AppendLine();
                }
                t.AppendLine(CultureInfo.InvariantCulture, $"Sortie `--json` : {SortieJson(c)}.{(c.Alias.Length > 0 ? $" Autre nom : {string.Join(", ", c.Alias.Select(a => $"`gestion-mep {a}`"))}." : "")}");
                t.AppendLine();
                if (c.Exemples.Length > 0)
                {
                    t.AppendLine("```bash");
                    foreach (var e in c.Exemples) t.AppendLine(e);
                    t.AppendLine("```");
                    t.AppendLine();
                }
            }
        }
        return t.ToString();
    }

    // ---------- Complétion ----------

    /// <summary>Options d'une commande (première forme de chaque option, et options citées dans l'usage).</summary>
    private static List<string> OptionsDe(Commande c) =>
        c.Options.SelectMany(o => o.Option.Split(',', StringSplitOptions.TrimEntries).Select(x => x.Split(' ')[0]))
            .Concat(c.Usage.Split(' ', '[', ']', '|').Where(m => m.StartsWith("--", StringComparison.Ordinal)))
            .Where(o => o.StartsWith('-')).Distinct().ToList();

    private static readonly string[] OptionsGlobalesCompletion = ["--json", "--quiet", "-q", "--verbose", "-v", "--non-interactif", "--config", "--help", "-h"];

    /// <summary>Anciens noms d'un seul mot (« sauvegarder », « journal »…), avec la commande qu'ils désignent.</summary>
    private static List<Commande> AliasSeuls() =>
        [.. Registre.SelectMany(c => c.Alias.Where(a => !a.StartsWith('-') && !a.Contains(' ')).Select(a => c with { Nom = a }))];

    /// <summary>Premiers mots proposés : groupes, commandes seules et anciens noms.</summary>
    private static List<string> MotsDeTete() => [.. Groupes.Select(g => g.Nom).Concat(AliasSeuls().Select(c => c.Nom)).Distinct()];

    private static int Completion(string shell)
    {
        var groupes = Registre.Where(c => c.Verbe.Length > 0).GroupBy(c => c.Groupe).ToList();
        var seules = Registre.Where(c => c.Verbe.Length == 0).Concat(AliasSeuls()).ToList();
        var valeurs = new Dictionary<string, string> { ["completion"] = "bash zsh powershell", ["aide"] = string.Join(' ', Groupes.Select(g => g.Nom)) };
        string script;
        switch (shell.ToLowerInvariant())
        {
            case "bash" or "zsh":
            {
                var t = new StringBuilder();
                t.AppendLine(CultureInfo.InvariantCulture, $"# Complétion de gestion-mep ({shell}), produite par « gestion-mep completion {shell} ».");
                if (shell == "zsh")
                {
                    t.AppendLine("# Dans ~/.zshrc : source <(gestion-mep completion zsh)");
                    t.AppendLine("autoload -U +X compinit && compinit");
                    t.AppendLine("autoload -U +X bashcompinit && bashcompinit");
                }
                else t.AppendLine("# À poser dans /etc/bash_completion.d/gestion-mep, ou : source <(gestion-mep completion bash)");
                t.AppendLine("_gestion_mep()");
                t.AppendLine("{");
                t.AppendLine("    local cur=\"${COMP_WORDS[COMP_CWORD]}\" i actions=\"\" options=\"\"");
                t.AppendLine("    local -a mots=()");
                t.AppendLine("    for ((i = 1; i < COMP_CWORD; i++)); do");
                t.AppendLine("        case \"${COMP_WORDS[i]}\" in -*) ;; *) mots+=(\"${COMP_WORDS[i]}\") ;; esac");
                t.AppendLine("    done");
                t.AppendLine(CultureInfo.InvariantCulture, $"    local globales=\"{string.Join(' ', OptionsGlobalesCompletion)}\"");
                t.AppendLine("    if [ ${#mots[@]} -eq 0 ] && [[ \"$cur\" != -* ]]; then");
                t.AppendLine(CultureInfo.InvariantCulture, $"        COMPREPLY=( $(compgen -W \"{string.Join(' ', MotsDeTete())}\" -- \"$cur\") )");
                t.AppendLine("        return");
                t.AppendLine("    fi");
                t.AppendLine("    case \"${mots[0]}\" in");
                foreach (var g in groupes) t.AppendLine(CultureInfo.InvariantCulture, $"        {g.Key}) actions=\"{string.Join(' ', g.Select(c => c.Verbe))}\" ;;");
                foreach (var (nom, v) in valeurs) t.AppendLine(CultureInfo.InvariantCulture, $"        {nom}) actions=\"{v}\" ;;");
                t.AppendLine("    esac");
                t.AppendLine("    if [ ${#mots[@]} -eq 1 ] && [ -n \"$actions\" ] && [[ \"$cur\" != -* ]]; then");
                t.AppendLine("        COMPREPLY=( $(compgen -W \"$actions\" -- \"$cur\") )");
                t.AppendLine("        return");
                t.AppendLine("    fi");
                t.AppendLine("    case \"${mots[0]} ${mots[1]}\" in");
                foreach (var c in Registre.Where(c => c.Verbe.Length > 0))
                    t.AppendLine(CultureInfo.InvariantCulture, $"        \"{c.Nom}\") options=\"{string.Join(' ', OptionsDe(c))}\" ;;");
                foreach (var c in seules) t.AppendLine(CultureInfo.InvariantCulture, $"        \"{c.Nom} \"*) options=\"{string.Join(' ', OptionsDe(c))}\" ;;");
                t.AppendLine("    esac");
                t.AppendLine("    COMPREPLY=( $(compgen -W \"$options $globales\" -- \"$cur\") )");
                t.AppendLine("}");
                t.AppendLine("complete -F _gestion_mep gestion-mep");
                script = t.ToString();
                break;
            }
            case "powershell" or "pwsh":
            {
                static string Liste(IEnumerable<string> mots) => "@(" + string.Join(", ", mots.Select(m => $"'{m}'")) + ")";
                var t = new StringBuilder();
                t.AppendLine("# Complétion de gestion-mep (PowerShell), produite par « gestion-mep completion powershell ».");
                t.AppendLine("# Dans $PROFILE : gestion-mep completion powershell | Out-String | Invoke-Expression");
                t.AppendLine("Register-ArgumentCompleter -Native -CommandName 'gestion-mep', 'gestion-mep.cmd' -ScriptBlock {");
                t.AppendLine("    param($motCourant, $ast, $position)");
                t.AppendLine(CultureInfo.InvariantCulture, $"    $tete = {Liste(MotsDeTete())}");
                t.AppendLine(CultureInfo.InvariantCulture, $"    $globales = {Liste(OptionsGlobalesCompletion)}");
                t.AppendLine("    $actions = @{");
                foreach (var g in groupes) t.AppendLine(CultureInfo.InvariantCulture, $"        '{g.Key}' = {Liste(g.Select(c => c.Verbe))}");
                foreach (var (nom, v) in valeurs) t.AppendLine(CultureInfo.InvariantCulture, $"        '{nom}' = {Liste(v.Split(' '))}");
                t.AppendLine("    }");
                t.AppendLine("    $options = @{");
                foreach (var c in Registre.Where(c => c.Verbe.Length > 0).Concat(seules)) t.AppendLine(CultureInfo.InvariantCulture, $"        '{c.Nom}' = {Liste(OptionsDe(c))}");
                t.AppendLine("    }");
                t.AppendLine("    $mots = @($ast.CommandElements | Select-Object -Skip 1 | ForEach-Object { $_.ToString() } | Where-Object { $_ -notlike '-*' })");
                t.AppendLine("    if ($motCourant -and $mots.Count -gt 0 -and $mots[-1] -eq $motCourant) { $mots = @($mots | Select-Object -SkipLast 1) }");
                t.AppendLine("    if ($mots.Count -eq 0 -and $motCourant -notlike '-*') { $candidats = $tete }");
                t.AppendLine("    elseif ($mots.Count -eq 1 -and $actions.ContainsKey($mots[0]) -and $motCourant -notlike '-*') { $candidats = $actions[$mots[0]] }");
                t.AppendLine("    else {");
                t.AppendLine("        $candidats = @($options[\"$($mots[0]) $($mots[1])\"]) + @($options[$mots[0]]) + $globales");
                t.AppendLine("    }");
                t.AppendLine("    $candidats | Where-Object { $_ -and $_ -like \"$motCourant*\" } | ForEach-Object {");
                t.AppendLine("        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)");
                t.AppendLine("    }");
                t.AppendLine("}");
                script = t.ToString();
                break;
            }
            default:
                throw new ErreurUsage($"shell inconnu « {shell} » : bash, zsh ou powershell");
        }
        S.DonneesEcrites = true; // le script tel quel, même avec --json
        Console.Write(script);
        return 0;
    }
}
