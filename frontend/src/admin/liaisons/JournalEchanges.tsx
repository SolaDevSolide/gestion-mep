import { ArrowDownLeft, ArrowUpRight, RefreshCw } from 'lucide-react';
import { useState } from 'react';
import { api } from '../../api';
import { useAsync } from '../../hooks';
import type { EchangeLiaison } from '../../types';
import { Button } from '../../ui/Button';
import { Card, CardHeader } from '../../ui/Card';
import { ErreurChargement } from '../../ui/ErreurChargement';
import { Segmented } from '../../ui/Segmented';
import { EnChargement, Squelette } from '../../ui/Squelette';
import { StatusPill } from '../../ui/StatusPill';
import { quandListe } from '../connexions/connexions';
import { libelleOperation, resultatEchange } from './liaisons';

type Filtre = 'tous' | 'refuses' | 'envoyes' | 'recus';
const PAGE = 20;
const COLONNES = 'md:grid-cols-[7.5rem_6rem_minmax(0,1fr)_8rem]';

/** Garde les échanges du filtre (le serveur filtre les refus ; le sens se filtre ici). */
function garder(f: Filtre, e: EchangeLiaison) {
  if (f === 'envoyes') return e.sens === 'sortant';
  if (f === 'recus') return e.sens === 'entrant';
  return true;
}

/** Journal des échanges d'une liaison (90 jours), le plus récent d'abord, chargé à part de la fiche. */
export function JournalEchanges({ liaisonId, tick }: { liaisonId: string; tick: number }) {
  const [filtre, setFiltre] = useState<Filtre>('tous');
  const [suite, setSuite] = useState<EchangeLiaison[][]>([]);
  const [plus, setPlus] = useState(false);
  const limite = filtre === 'envoyes' || filtre === 'recus' ? 3 * PAGE : PAGE;
  const page = useAsync(() => {
    setSuite([]);
    return api.echangesLiaisons({ liaison: liaisonId, limite, refus: filtre === 'refuses' });
  }, [liaisonId, filtre, tick]);
  const pages = page.donnees ? [page.donnees, ...suite] : [];
  const tous = pages.flat();
  const echanges = tous.filter((e) => garder(filtre, e));
  const derniere = pages.length > 0 ? pages[pages.length - 1] : [];
  const encore = derniere.length === limite;

  async function plusAnciennes() {
    const avant = tous[tous.length - 1]?.quand;
    if (!avant) return;
    setPlus(true);
    try {
      const p = await api.echangesLiaisons({ liaison: liaisonId, limite, refus: filtre === 'refuses', avant });
      setSuite((s) => [...s, p]);
    } finally {
      setPlus(false);
    }
  }

  return (
    <Card className="min-w-0">
      <CardHeader
        titre="Journal des échanges"
        description="Dans les deux sens, refus compris. Conservé 90 jours."
        action={
          <div className="flex flex-wrap items-center gap-2">
            <Segmented<Filtre>
              label="Échanges montrés"
              valeur={filtre}
              onChange={setFiltre}
              options={[
                { valeur: 'tous', libelle: 'Tous' },
                { valeur: 'refuses', libelle: 'Refusés' },
                { valeur: 'envoyes', libelle: 'Envoyés' },
                { valeur: 'recus', libelle: 'Reçus' },
              ]}
            />
            <Button
              taille="sm"
              icone={<RefreshCw className="size-4" aria-hidden />}
              chargement={page.rechargement}
              onClick={page.recharger}
            >
              Actualiser
            </Button>
          </div>
        }
      />
      <div
        role="row"
        className={`hidden gap-4 border-b border-line bg-surface-2/50 px-5 py-2 text-2xs font-semibold tracking-wider text-fg-subtle uppercase md:grid ${COLONNES}`}
      >
        <span>Quand</span>
        <span>Sens</span>
        <span>Opération</span>
        <span>Résultat</span>
      </div>
      {page.erreur && (
        <div className="p-5">
          <ErreurChargement
            erreur={page.echec}
            titre="Impossible de charger le journal des échanges"
            onReessayer={page.recharger}
            enCours={page.chargement}
          />
        </div>
      )}
      {!page.erreur && !page.donnees && (
        <EnChargement libelle="Chargement des échanges…">
          <ul className="divide-y divide-line">
            {Array.from({ length: 6 }, (_, i) => (
              <li key={i} className={`grid gap-2 px-5 py-3.5 md:gap-4 ${COLONNES}`}>
                <Squelette className="h-4 w-20" />
                <Squelette className="h-4 w-14" />
                <div className="grid gap-1.5">
                  <Squelette className="h-4 w-2/5" />
                  <Squelette className="h-3 w-4/5" />
                </div>
                <Squelette className="h-5 w-16 rounded-full" />
              </li>
            ))}
          </ul>
        </EnChargement>
      )}
      {page.donnees && echanges.length === 0 && (
        <p className="px-5 py-8 text-center text-sm text-fg-subtle">
          {filtre === 'refuses'
            ? 'Aucune requête refusée.'
            : filtre === 'envoyes'
              ? 'Aucun échange envoyé à Noosphere.'
              : filtre === 'recus'
                ? 'Aucun échange reçu de Noosphere.'
                : 'Aucun échange pour l’instant.'}
        </p>
      )}
      {echanges.length > 0 && (
        <>
          <ul className="apparition divide-y divide-line">
            {echanges.map((e) => {
              const r = resultatEchange(e.resultat);
              const entrant = e.sens === 'entrant';
              return (
                <li key={e.id} className={`grid gap-1 px-5 py-3 text-sm md:items-start md:gap-4 ${COLONNES}`}>
                  <time dateTime={e.quand} className="font-mono text-ui text-fg-subtle tabular-nums">
                    {quandListe(e.quand)}
                  </time>
                  <span className="inline-flex items-center gap-1.5 text-fg-muted">
                    {entrant ? (
                      <ArrowDownLeft className="size-3.5" aria-hidden />
                    ) : (
                      <ArrowUpRight className="size-3.5" aria-hidden />
                    )}
                    {entrant ? 'Reçu' : 'Envoyé'}
                  </span>
                  <span className="min-w-0">
                    <span className="block font-medium">{libelleOperation(e)}</span>
                    <span className="block text-ui text-fg-subtle [overflow-wrap:anywhere]">
                      {e.resultat === 'refuse' && `/link/v1/${e.operation} · `}
                      {e.detail}
                      {e.code !== null && ` (${e.code})`}
                    </span>
                  </span>
                  <span>
                    <StatusPill ton={r.ton} taille="sm">
                      {r.libelle}
                    </StatusPill>
                  </span>
                </li>
              );
            })}
          </ul>
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-b-xl border-t border-line px-5 py-3">
            <p className="text-ui text-fg-subtle">
              {echanges.length} ligne{echanges.length > 1 ? 's' : ''} · plus récentes d’abord
            </p>
            {encore && (
              <Button taille="sm" chargement={plus} onClick={() => void plusAnciennes()}>
                Plus anciennes
              </Button>
            )}
          </div>
        </>
      )}
    </Card>
  );
}
