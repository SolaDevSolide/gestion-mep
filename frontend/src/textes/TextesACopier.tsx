import { Copy, ExternalLink, FileText, Mail, Settings2, TriangleAlert, UsersRound } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useAsync } from '../hooks';
import { useReferentiel } from '../referentiels';
import type { Mep } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { cn } from '../ui/cn';
import { EmptyState } from '../ui/EmptyState';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Select } from '../ui/Input';
import { Segmented } from '../ui/Segmented';
import { Badge } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';
import { ApercuTexte } from './ApercuTexte';
import { EnChargement, Squelette, SqueletteLignes } from '../ui/Squelette';
import {
  copier,
  enHtml,
  enTexteSimple,
  generer,
  libellesDepuis,
  manquantes,
  texteManque,
  valeursDepuis,
} from './modelesTexte';

export type FormatTexte = 'forme' | 'simple';

/** Liste lisible des informations manquantes : « date de la MEP, heure limite ». */
export const listeManquantes = (cles: string[], libelles: ReadonlyMap<string, string>) =>
  cles.map((c) => (libelles.get(c) ?? c).toLocaleLowerCase('fr')).join(', ');

/**
 * Choisir un modèle d'une équipe (par défaut celle du pilote), voir le texte rempli avec les informations de la MEP,
 * copier l'objet et le texte (mis en forme ou simple) en un clic. Rien n'est envoyé.
 */
export function TextesACopierContenu({ mep }: { mep: Pick<Mep, 'id' | 'version'> }) {
  const toast = useToast();
  const [equipe, setEquipe] = useState<string | null>(null);
  const [choisi, setChoisi] = useState<string | null>(null);
  const [format, setFormat] = useState<FormatTexte>('forme');

  const equipes = useReferentiel('equipes');
  const catalogue = useAsync(() => api.variablesModelesTexte(), []);
  // Valeurs recalculées à chaque version de la MEP (une modification de la section Communication se voit aussitôt).
  const valeurs = useAsync(() => api.variablesTexte(mep.id, equipe ?? undefined), [mep.id, mep.version, equipe]);
  const equipeId = equipe ?? valeurs.donnees?.equipeId ?? null;
  const modeles = useAsync(() => (equipeId ? api.modelesTexte(equipeId) : Promise.resolve([])), [equipeId]);

  useEffect(() => setChoisi(null), [equipeId]);

  const libelles = useMemo(() => libellesDepuis(catalogue.donnees ?? []), [catalogue.donnees]);
  const vals = useMemo(() => valeursDepuis(valeurs.donnees?.valeurs ?? []), [valeurs.donnees]);
  const liste = modeles.donnees ?? [];
  const modele = liste.find((m) => m.id === choisi) ?? liste[0];

  const objet = modele ? generer(modele.objet, vals) : [];
  const corps = modele ? generer(modele.corps, vals) : [];
  const manque = modele ? manquantes(`${modele.objet}\n${modele.corps}`, vals) : [];
  const nomEquipe = equipes.donnees?.find((e) => e.id === equipeId)?.nom ?? '';

  async function copierObjet() {
    try {
      await copier(enTexteSimple(objet, libelles));
      toast.succes('Objet copié.');
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Copie impossible.');
    }
  }

  async function copierTexte() {
    try {
      const simple = enTexteSimple(corps, libelles);
      if (format === 'forme') {
        await copier(simple, enHtml(corps, libelles));
        toast.succes('Texte mis en forme copié : collez-le dans votre messagerie.');
      } else {
        await copier(simple);
        toast.succes('Texte simple copié.');
      }
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Copie impossible.');
    }
  }

  const echec = equipes.erreur ?? valeurs.echec ?? modeles.echec ?? catalogue.echec;
  if (echec)
    return (
      <ErreurChargement
        erreur={echec}
        titre="Impossible de charger les textes à copier"
        onReessayer={() => {
          if (equipes.erreur) equipes.recharger();
          if (valeurs.echec) valeurs.recharger();
          if (modeles.echec) modeles.recharger();
          if (catalogue.echec) catalogue.recharger();
        }}
        enCours={equipes.enCours || valeurs.chargement || modeles.chargement || catalogue.chargement}
      />
    );
  if (equipes.donnees !== null && equipes.donnees.length === 0)
    return (
      <EmptyState
        icone={UsersRound}
        titre="Aucune équipe"
        description="Les modèles de texte appartiennent aux équipes : un administrateur crée les équipes dans la console d’administration."
      />
    );

  if (equipes.donnees === null || modeles.donnees === null || valeurs.donnees === null)
    return (
      <EnChargement libelle="Chargement des textes…" className="space-y-4">
        <Squelette className="ml-auto h-9 w-72 rounded-lg" />
        <div className="grid gap-5 lg:grid-cols-[14rem_minmax(0,1fr)]">
          <SqueletteLignes lignes={4} className="content-start gap-4" />
          <div className="grid gap-3">
            <Squelette className="h-9 w-full rounded-lg" />
            <Squelette className="h-64 w-full rounded-lg" />
          </div>
        </div>
      </EnChargement>
    );

  return (
    <div className="apparition space-y-4">
      <div className="flex flex-wrap items-center justify-end gap-2">
        <span className="text-ui text-fg-subtle">Modèles de</span>
        <div className="w-60">
          <Select aria-label="Équipe" value={equipeId ?? ''} onChange={(e) => setEquipe(e.target.value || null)}>
            {(equipes.donnees ?? []).map((e) => (
              <option key={e.id} value={e.id}>
                {e.nom}
                {e.id === valeurs.donnees?.equipeProposeeId ? ' (équipe du pilote)' : ''}
              </option>
            ))}
          </Select>
        </div>
      </div>
      <div className="grid gap-5 lg:grid-cols-[14rem_minmax(0,1fr)]">
        <div>
          <ul className="space-y-1" aria-label="Modèles de texte">
            {liste.map((m) => {
              const actif = m.id === modele?.id;
              return (
                <li key={m.id}>
                  <button
                    type="button"
                    aria-current={actif ? 'true' : undefined}
                    onClick={() => setChoisi(m.id)}
                    className={cn(
                      'flex w-full items-start gap-2.5 rounded-lg px-2.5 py-2 text-left text-sm font-medium transition-colors duration-150',
                      actif ? 'bg-accent-soft text-accent-text' : 'text-fg-muted hover:bg-surface-2 hover:text-fg',
                    )}
                  >
                    <FileText className="mt-0.5 size-4 shrink-0" aria-hidden />
                    <span className="min-w-0 flex-1 leading-tight">{m.nom}</span>
                  </button>
                </li>
              );
            })}
          </ul>
          {equipeId && (
            <Link
              to={`/parametres/textes?equipe=${equipeId}`}
              className="mt-3 flex items-center gap-1.5 px-2.5 text-ui text-accent-text hover:underline"
            >
              <Settings2 className="size-3.5" aria-hidden /> Gérer les modèles de l’équipe
            </Link>
          )}
        </div>

        {modele && (
          <div className="min-w-0 space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <Segmented<FormatTexte>
                label="Format du texte"
                valeur={format}
                onChange={setFormat}
                options={[
                  { valeur: 'forme', libelle: 'Mis en forme' },
                  { valeur: 'simple', libelle: 'Texte simple' },
                ]}
              />
              <div className="flex flex-wrap gap-2">
                <Button taille="sm" icone={<Copy className="size-4" aria-hidden />} onClick={() => void copierObjet()}>
                  Copier l’objet
                </Button>
                <Button
                  variante="primaire"
                  taille="sm"
                  icone={<Copy className="size-4" aria-hidden />}
                  onClick={() => void copierTexte()}
                >
                  {format === 'forme' ? 'Copier le texte mis en forme' : 'Copier le texte simple'}
                </Button>
              </div>
            </div>

            {manque.length > 0 && (
              <Alerte ton="warn" icone={TriangleAlert}>
                {manque.length === 1 ? 'Une information manque' : `${manque.length} informations manquent`} :{' '}
                {listeManquantes(manque, libelles)}. Complétez la MEP, ou le texte copié contiendra «{' '}
                {texteManque(manque[0], libelles)} ».
              </Alerte>
            )}

            <div className="overflow-hidden rounded-lg border border-line">
              <div className="flex items-baseline gap-3 border-b border-line bg-surface-2/50 px-4 py-2.5 text-sm">
                <span className="w-12 shrink-0 text-ui text-fg-subtle">Objet</span>
                <ApercuTexte lignes={objet} libelles={libelles} simple={format === 'simple'} className="font-medium" />
              </div>
              <div className="max-h-[30rem] overflow-y-auto px-4 py-4" aria-label="Texte rempli">
                <ApercuTexte lignes={corps} libelles={libelles} simple={format === 'simple'} />
              </div>
            </div>
            <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-fg-subtle">
              {modele.fourni && <Badge ton="done">Fourni par défaut</Badge>}
              <span>
                Modèle « {modele.nom} » de l’équipe {nomEquipe}. Rien n’est envoyé : le texte est seulement copié.
              </span>
              <Link
                to={`/parametres/textes/${modele.id}`}
                className="inline-flex items-center gap-1 text-accent-text hover:underline"
              >
                {modele.peutModifier ? 'Modifier le modèle' : 'Voir le modèle'}{' '}
                <ExternalLink className="size-3" aria-hidden />
              </Link>
            </p>
          </div>
        )}
      </div>
    </div>
  );
}

/** Carte « Textes à copier » (section Communication de la préparation). */
export function TextesACopier({ mep }: { mep: Pick<Mep, 'id' | 'version'> }) {
  return (
    <Card>
      <CardHeader
        titre={
          <span className="flex items-center gap-2.5">
            <Mail className="size-4 text-accent" aria-hidden />
            Textes à copier
          </span>
        }
        description="Choisissez un modèle : le texte se remplit avec les informations de la MEP. Copiez-le dans votre messagerie."
      />
      <div className="px-5 py-5">
        <TextesACopierContenu mep={mep} />
      </div>
    </Card>
  );
}
