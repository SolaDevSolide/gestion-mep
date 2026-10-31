import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api';
import type { CatalogueLiaison, LiaisonNoosphere } from '../../types';
import { Card } from '../../ui/Card';
import { ErreurChargement } from '../../ui/ErreurChargement';
import { EnChargement, Squelette, SqueletteLignes } from '../../ui/Squelette';
import { Appairage } from './Appairage';
import { ConfirmationEmpreinte } from './ConfirmationEmpreinte';
import { LiaisonActive } from './LiaisonActive';
import { liaisonCourante } from './liaisons';
import { SuiviApplications } from './SuiviApplications';

interface Etat {
  liaisons: LiaisonNoosphere[];
  /** Dernier catalogue de Noosphere lu, pour la liaison en service confirmée ici. */
  catalogue: CatalogueLiaison | null;
  /** L'état de Noosphere n'a pas pu être relu à l'ouverture : la raison. */
  avertissement: string | null;
}

/**
 * Charge l'onglet en une fois : les liaisons, l'état de Noosphere relu pour la liaison en service (pour ne jamais montrer
 * un état du pair périmé) et le catalogue lu. `relire` : relire aussi l'état chez Noosphere (ouverture de l'onglet).
 */
async function charger(relire: boolean): Promise<Etat> {
  let liaisons = await api.liaisons();
  let avertissement: string | null = null;
  let courante = liaisonCourante(liaisons);
  if (courante && relire && !courante.demo) {
    try {
      const maj = await api.statutLiaison(courante.id);
      liaisons = liaisons.map((l) => (l.id === maj.id ? maj : l));
      courante = maj;
    } catch (e) {
      avertissement = e instanceof Error ? e.message : 'Noosphere injoignable.';
    }
  }
  const catalogue = courante?.confirmeeIci ? await api.catalogueLiaison(courante.id) : null;
  return { liaisons, catalogue, avertissement };
}

/** Rien n'est encore suivi alors que le catalogue est lu : le choix des applications s'ouvre de lui-même. */
const premierChoix = (l: LiaisonNoosphere, c: CatalogueLiaison | null) =>
  c?.luLe != null && l.systemesSuivis === 0 && l.domainesSuivis.length === 0 && !c.applications.some((a) => a.suivie);

/**
 * Onglet « Liaisons » de la console (#123) : appairer avec Noosphere, confirmer l'empreinte, choisir les applications
 * suivies et les rapprocher du catalogue, puis suivre la liaison active (synchronisation, journal, clés, coupure).
 */
export default function LiaisonsTab() {
  const [etat, setEtat] = useState<Etat | null>(null);
  const [echec, setEchec] = useState<unknown>(null);
  const [enCours, setEnCours] = useState(false);
  const [edition, setEdition] = useState(false);

  const lire = useCallback(async (relire: boolean) => {
    setEnCours(true);
    try {
      const e = await charger(relire);
      setEtat(e);
      setEchec(null);
    } catch (x) {
      setEchec(x ?? new Error('Chargement impossible.'));
    } finally {
      setEnCours(false);
    }
  }, []);

  // Ouverture de l'onglet : tout est lu, état de Noosphere compris, avant d'afficher quoi que ce soit.
  useEffect(() => {
    let annule = false;
    charger(true)
      .then((e) => {
        if (!annule) setEtat(e);
      })
      .catch((x: unknown) => {
        if (!annule) setEchec(x ?? new Error('Chargement impossible.'));
      });
    return () => {
      annule = true;
    };
  }, []);

  if (echec !== null && !etat)
    return (
      <ErreurChargement
        erreur={echec}
        titre="Impossible de charger la liaison avec Noosphere"
        onReessayer={() => void lire(true)}
        enCours={enCours}
      />
    );
  if (!etat) return <SqueletteOnglet />;

  const courante = liaisonCourante(etat.liaisons);
  const coupees = etat.liaisons.filter((l) => l.etat === 'Coupee');
  // Une liaison qui change (confirmée, options) : remplacée dans la liste ; confirmée ici, le catalogue est relu.
  const remplacer = (l: LiaisonNoosphere, catalogue?: CatalogueLiaison) =>
    setEtat((e) =>
      e
        ? {
            ...e,
            liaisons: e.liaisons.some((x) => x.id === l.id)
              ? e.liaisons.map((x) => (x.id === l.id ? l : x))
              : [l, ...e.liaisons],
            catalogue: catalogue ?? e.catalogue,
          }
        : e,
    );

  if (!courante)
    return (
      <div className="apparition">
        <Appairage coupees={coupees} onAppairee={(l) => remplacer(l)} />
      </div>
    );

  if (!courante.confirmeeIci)
    return (
      <div className="apparition">
        <ConfirmationEmpreinte
          liaison={courante}
          onChange={(l) => {
            if (l.confirmeeIci) void lire(true);
            else remplacer(l);
          }}
        />
      </div>
    );

  if (etat.catalogue && (edition || premierChoix(courante, etat.catalogue)))
    return (
      <div className="apparition">
        <SuiviApplications
          key={etat.catalogue.luLe ?? ''}
          liaison={courante}
          catalogue={etat.catalogue}
          onTermine={(c, l) => {
            setEdition(false);
            remplacer(l, c);
          }}
          onAnnuler={premierChoix(courante, etat.catalogue) ? undefined : () => setEdition(false)}
        />
      </div>
    );

  return (
    <div className="apparition">
      <LiaisonActive
        liaison={courante}
        catalogue={etat.catalogue}
        avertissement={etat.avertissement}
        onChange={remplacer}
        onCoupee={() => void lire(false)}
        onModifierSuivi={() => setEdition(true)}
        onRelireStatut={() => lire(true)}
      />
    </div>
  );
}

/** Squelette de l'onglet : la fiche de la liaison et, dessous, le journal et le résumé. */
function SqueletteOnglet() {
  return (
    <EnChargement libelle="Chargement de la liaison avec Noosphere…">
      <div className="grid gap-5">
        <Card className="p-5">
          <Squelette className="h-5 w-48" />
          <Squelette className="mt-2 h-4 w-80 max-w-full" />
          <SqueletteLignes lignes={5} className="mt-6 gap-3" />
        </Card>
        <div className="grid gap-5 lg:grid-cols-[minmax(0,1.75fr)_minmax(0,1fr)]">
          <Card className="p-5">
            <Squelette className="h-5 w-44" />
            <SqueletteLignes lignes={6} className="mt-5 gap-3" />
          </Card>
          <Card className="p-5">
            <Squelette className="h-5 w-40" />
            <SqueletteLignes lignes={3} className="mt-5 gap-3" />
          </Card>
        </div>
      </div>
    </EnChargement>
  );
}
