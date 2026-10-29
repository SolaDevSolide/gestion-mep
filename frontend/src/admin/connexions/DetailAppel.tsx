import { Pencil, Send, TriangleAlert } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api';
import { formatJourLong } from '../../format';
import { useAsync } from '../../hooks';
import type { Appel } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { CodeBlock } from '../../ui/CodeBlock';
import { ErreurChargement } from '../../ui/ErreurChargement';
import { Sheet } from '../../ui/Sheet';
import { EnChargement, Squelette, SqueletteLignes } from '../../ui/Squelette';
import { StatusPill } from '../../ui/StatusPill';
import { useToast } from '../../ui/Toast';
import { codeAppel, contexteAppel, duree, heureCourte, LIBELLE_ORIGINE, methode, resultatAppel } from './connexions';

const heureSecondes = new Intl.DateTimeFormat('fr-FR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });

/**
 * Détail d'un appel (panneau latéral de la console) : requête envoyée (secrets masqués), tentatives et réponses, message.
 * « Renvoyer » crée un nouvel appel avec le même message ; « Copier la requête » donne la commande curl équivalente.
 */
export function DetailAppel({
  appelId,
  onFermer,
  onRenvoye,
}: {
  appelId: string | null;
  onFermer: () => void;
  onRenvoye?: () => void;
}) {
  return (
    <Sheet
      open={appelId !== null}
      onOpenChange={(o) => {
        if (!o) onFermer();
      }}
      titre="Détail de l’appel"
      className="max-w-[640px]"
    >
      {appelId && <Contenu appelId={appelId} onRenvoye={onRenvoye} />}
    </Sheet>
  );
}

function Contenu({ appelId, onRenvoye }: { appelId: string; onRenvoye?: () => void }) {
  const { donnees, echec, chargement, recharger } = useAsync(() => api.appelApi(appelId), [appelId]);
  if (echec)
    return (
      <ErreurChargement
        erreur={echec}
        titre="Impossible de charger l’appel"
        introuvable="Cet appel n’existe plus : il a été supprimé avec sa connexion, ou l’historique a été purgé."
        onReessayer={recharger}
        enCours={chargement}
      />
    );
  if (!donnees)
    return (
      <EnChargement libelle="Chargement de l’appel…">
        <Squelette className="h-6 w-56" />
        <SqueletteLignes lignes={6} className="mt-5" />
        <Squelette className="mt-5 h-40 w-full" />
      </EnChargement>
    );
  return <Detail appel={donnees} onRenvoye={onRenvoye} />;
}

function Detail({ appel: d, onRenvoye }: { appel: Appel; onRenvoye?: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const a = d.appel;
  const r = resultatAppel(a);
  const termine = a.etat === 'Reussi' || a.etat === 'Echec';
  const entetes = d.entetes.map((e) => `${e.nom}: ${e.valeur}`).join('\n');

  async function renvoyer() {
    try {
      await api.renvoyerAppelApi(a.id);
      toast.succes('Nouvel appel en file : il part tout de suite.');
      onRenvoye?.();
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Renvoi impossible.');
    }
  }

  return (
    <div className="apparition grid gap-5 text-sm">
      <div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusPill ton={r.ton}>{r.libelle}</StatusPill>
          <span className="font-mono text-ui text-fg-muted">{codeAppel(a)}</span>
        </div>
        <p className="mt-2 font-medium text-fg">
          {a.connexion} · {contexteAppel(a)}
        </p>
        <p className="mt-1 text-fg-muted">
          {LIBELLE_ORIGINE[a.origine]}
          {a.origine === 'Automatique' ? ` : « ${a.libelleEvenement} »` : ''}
          {a.demandePar ? ` par ${a.demandePar}` : ''}, {formatJourLong(a.creeLe)} à {heureCourte(a.creeLe)}.
        </p>
        {a.resume && <p className="mt-1 text-fg-subtle">{a.resume}</p>}
      </div>

      <section aria-label="Requête">
        <h3 className="text-ui font-semibold text-fg">Requête</h3>
        <p className="mt-1.5 font-mono text-ui break-all">
          <span className="font-semibold">{methode(d.methode)}</span> {d.url}
        </p>
        {entetes && <CodeBlock titre="En-têtes" code={entetes} className="mt-2" hauteurMax="max-h-40" />}
      </section>

      <section aria-label="Tentatives">
        <h3 className="text-ui font-semibold text-fg">Tentatives</h3>
        {d.tentatives.length === 0 ? (
          <p className="mt-1.5 text-fg-subtle">Aucune tentative pour l’instant : l’appel est en file.</p>
        ) : (
          <ol className="mt-1.5 grid gap-2">
            {d.tentatives.map((t, i) => (
              <li key={i} className="rounded-lg border border-line px-3 py-2">
                <p className="tabular-nums">
                  <span className="text-fg-subtle">{heureSecondes.format(new Date(t.debut))}</span>
                  {' · '}
                  <span className="font-medium">
                    {t.code !== null ? `${t.code}${t.libelle ? ` ${t.libelle}` : ''}` : t.libelle || 'Pas de réponse'}
                  </span>
                  {t.dureeMs > 0 && <span className="text-fg-subtle"> · {duree(t.dureeMs)}</span>}
                </p>
                {t.erreur && <p className="mt-1 text-danger-fg">{t.erreur}</p>}
                {t.extraitReponse && (
                  <pre className="mt-1.5 max-h-32 overflow-auto rounded-md bg-code-bg px-2 py-1.5 font-mono text-xs whitespace-pre-wrap text-code-fg">
                    {t.entetesReponse ? `${t.entetesReponse}\n` : ''}
                    {t.extraitReponse}
                  </pre>
                )}
              </li>
            ))}
          </ol>
        )}
        {a.etat === 'Echec' && a.origine !== 'Essai' && (
          <Alerte ton="danger" icone={TriangleAlert} className="mt-2">
            Abandonné après {a.tentatives} tentative{a.tentatives > 1 ? 's' : ''} : connexion « En erreur » jusqu’au
            prochain appel réussi.
          </Alerte>
        )}
      </section>

      {d.corps && (
        <section aria-label="Message envoyé">
          <h3 className="text-ui font-semibold text-fg">Message envoyé</h3>
          <pre
            tabIndex={0}
            className="mt-1.5 max-h-80 overflow-auto rounded-lg border border-line bg-code-bg px-3 py-2.5 font-mono text-xs leading-relaxed break-words whitespace-pre-wrap text-code-fg"
          >
            {d.corps}
          </pre>
        </section>
      )}

      <div className="flex flex-wrap gap-2 border-t border-line pt-4">
        {termine && a.origine !== 'Essai' && (
          <Button taille="sm" variante="primaire" icone={<Send className="size-4" aria-hidden />} action={renvoyer}>
            Renvoyer
          </Button>
        )}
        <Button
          taille="sm"
          action={async () => {
            try {
              await navigator.clipboard.writeText(d.curl);
              toast.succes('Requête copiée (commande curl, secrets remplacés par <secret>).');
            } catch {
              toast.erreur('Copie impossible : le navigateur n’autorise pas l’accès au presse-papiers.');
            }
          }}
        >
          Copier la requête
        </Button>
        {d.connexionModifiable && (
          <Button
            taille="sm"
            variante="fantome"
            icone={<Pencil className="size-4" aria-hidden />}
            onClick={() => void navigate(`/admin/api/${a.connexionId}`)}
          >
            Modifier la connexion
          </Button>
        )}
      </div>
    </div>
  );
}
