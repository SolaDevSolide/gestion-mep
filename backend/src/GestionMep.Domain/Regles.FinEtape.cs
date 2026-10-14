namespace GestionMep.Domain;

// Règles métier : fin d'une étape (test unitaire ou simple vérification) et suivi des tests unitaires.
public static partial class Regles
{
    public static void DefinirFin(Etape etape, FinEtape fin)
    {
        if (!Enum.IsDefined(fin)) throw new RegleMetierException("Fin d'étape inconnue : test unitaire ou simple vérification.");
        etape.Fin = fin;
    }

    /// <summary>
    /// Étapes de la MEP à prendre en compte dans le suivi des tests (et des simples vérifications), dans l'ordre du plan :
    /// le déploiement, puis le retour arrière seulement s'il a été déclenché (sinon ses étapes ne sont pas censées s'exécuter).
    /// </summary>
    public static IEnumerable<Etape> EtapesSuivies(Mep mep) =>
        mep.EtapesTriees().Where(e => e.Plan == TypePlan.Deploiement || mep.RetourArriereDeclencheLe is not null);

    /// <summary>Tests unitaires suivis de la MEP (voir <see cref="EtapesSuivies"/>).</summary>
    public static List<Etape> TestsUnitaires(Mep mep) => EtapesSuivies(mep).Where(e => e.Fin == FinEtape.TestUnitaire).ToList();

    /// <summary>Test fait : l'étape est terminée, réussie ou en échec (une étape reprise redevient à faire).</summary>
    public static bool TestFait(Etape e) => e.Etat is EtatEtape.Reussie or EtatEtape.Echec;
}
