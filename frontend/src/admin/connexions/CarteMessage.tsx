import { Braces, OctagonAlert, Plus, RotateCcw, Search, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { grouperVariables } from '../../parametres/ModeleTexteEditeurPage';
import { mettreEnFormeJson, suggestion, type CatalogueGabarit, type Rendu } from '../../textes/gabarits';
import type { FormatCorps, VariableTexte } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { BoutonIcone, Button } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { EditeurGabarit } from '../../ui/EditeurGabarit';
import { Input } from '../../ui/Input';
import { Segmented } from '../../ui/Segmented';
import { Tooltip } from '../../ui/Tooltip';
import type { Brouillon } from './brouillon';
import { TitreCarte } from './ChampsConnexion';
import { LIBELLE_FORMAT } from './connexions';

const FORMATS: FormatCorps[] = ['Json', 'Formulaire', 'Texte', 'Aucun'];

/** Le corps de la requête, rempli avec les informations de la MEP et de l'événement (carte 3 de l'éditeur). */
export function CarteMessage({
  b,
  maj,
  catalogue,
  variables,
  rendu,
  inconnues,
  corpsModele,
  surChamp,
  inserer,
}: {
  b: Brouillon;
  maj: (p: Partial<Brouillon>) => void;
  catalogue: CatalogueGabarit;
  variables: VariableTexte[];
  /** Message rendu avec des valeurs d'exemple (JSON vérifié). */
  rendu: Rendu | null;
  /** Variables inconnues de toute la connexion (adresse, en-têtes, message). */
  inconnues: string[];
  /** Message du modèle de départ (« Revenir au message du modèle »), s'il y en a un. */
  corpsModele: string | null;
  surChamp: (cle: string, el: HTMLTextAreaElement | null) => void;
  inserer: (cle: string) => void;
}) {
  const [recherche, setRecherche] = useState('');
  const [aRemettre, setARemettre] = useState(false);
  const groupes = grouperVariables(variables, recherche);
  const erreur = rendu?.erreurs[0] ?? null;
  const libelles = new Map(variables.map((v) => [v.cle, v.libelle]));
  const get = b.methode === 'Get';

  function allerA(ligne: number, colonne: number) {
    const el = document.getElementById('message-connexion');
    if (!(el instanceof HTMLTextAreaElement)) return;
    let i = 0;
    for (let l = 1; l < ligne; l++) i = b.corps.indexOf('\n', i) + 1;
    const pos = Math.min(b.corps.length, i + colonne - 1);
    el.focus();
    el.setSelectionRange(pos, pos);
  }

  function corriger(fausse: string, juste: string) {
    const re = new RegExp(`\\{\\{\\s*${fausse}\\s*\\}\\}`, 'g');
    maj({
      corps: b.corps.replace(re, `{{${juste}}}`),
      url: b.url.replace(re, `{{${juste}}}`),
      entetes: b.entetes.map((e) => ({ ...e, valeur: e.valeur.replace(re, `{{${juste}}}`) })),
      champs: b.champs.map((c) => ({ ...c, valeur: c.valeur.replace(re, `{{${juste}}}`) })),
    });
  }

  return (
    <Card className="grid gap-5 p-5">
      <TitreCarte
        numero={3}
        titre="Message"
        description="Le corps de la requête, rempli avec les informations de la MEP et de l’événement."
      />
      <div className="flex flex-wrap items-center gap-3">
        <span className="text-ui font-medium text-fg">Format</span>
        <Segmented<FormatCorps>
          label="Format du message"
          valeur={b.format}
          disabled={get}
          onChange={(f) => maj({ format: f })}
          options={FORMATS.map((f) => ({ valeur: f, libelle: LIBELLE_FORMAT[f] }))}
        />
        {get && <span className="text-xs text-fg-subtle">Une requête GET n’envoie pas de message.</span>}
      </div>

      {(b.format === 'Json' || b.format === 'Texte') && (
        <div className="grid gap-2">
          <label htmlFor="message-connexion" className="sr-only">
            Message
          </label>
          <EditeurGabarit
            id="message-connexion"
            value={b.corps}
            onChange={(v) => maj({ corps: v })}
            format={b.format}
            catalogue={catalogue}
            variables={variables}
            rows={14}
            erreur={erreur}
            aria-invalid={Boolean(erreur) || undefined}
            aria-describedby={erreur ? 'erreur-message-connexion' : undefined}
            onFocus={() => surChamp('corps', null)}
            ref={(el) => surChamp('corps', el)}
          />
          {erreur && (
            <Alerte
              ton="danger"
              icone={OctagonAlert}
              role="alert"
              titre={`${b.format === 'Json' ? 'JSON invalide' : 'Message invalide'}, ligne ${erreur.ligne}, colonne ${erreur.colonne}`}
              action={
                <Button taille="sm" onClick={() => allerA(erreur.ligne, erreur.colonne)}>
                  Aller à la ligne
                </Button>
              }
            >
              <span id="erreur-message-connexion">{erreur.message}</span>
            </Alerte>
          )}
          {b.format === 'Json' && (
            <div className="flex flex-wrap gap-2">
              <Button
                taille="sm"
                icone={<Braces className="size-4" aria-hidden />}
                disabled={Boolean(erreur)}
                onClick={() => {
                  const f = mettreEnFormeJson(b.corps, catalogue);
                  if (f !== null) maj({ corps: f });
                }}
              >
                Mettre en forme le JSON
              </Button>
              {corpsModele !== null && corpsModele !== b.corps && (
                <Button
                  taille="sm"
                  variante="fantome"
                  icone={<RotateCcw className="size-4" aria-hidden />}
                  onClick={() => setARemettre(true)}
                >
                  Revenir au message du modèle
                </Button>
              )}
            </div>
          )}
          {aRemettre && corpsModele !== null && (
            <Alerte
              ton="warn"
              icone={RotateCcw}
              action={
                <span className="flex gap-2">
                  <Button
                    taille="sm"
                    onClick={() => {
                      maj({ corps: corpsModele });
                      setARemettre(false);
                    }}
                  >
                    Remplacer le message
                  </Button>
                  <Button taille="sm" variante="fantome" onClick={() => setARemettre(false)}>
                    Garder le mien
                  </Button>
                </span>
              }
            >
              Le message du modèle de départ remplace le vôtre.
            </Alerte>
          )}
          {b.format === 'Json' && (
            <p className="text-xs text-fg-subtle">
              Écrivez les variables entre guillemets (<code className="font-mono">"{'{{pilote}}'}"</code>) : la valeur y
              est échappée. Seules les listes (tableau JSON) et <code className="font-mono">{'{{donnees_json}}'}</code>{' '}
              (objet) s’écrivent hors guillemets. Une valeur absente part vide.{' '}
              <code className="font-mono">**gras**</code> et les puces ne sont pas interprétés.
            </p>
          )}
        </div>
      )}

      {b.format === 'Formulaire' && (
        <div className="grid gap-2">
          {b.champs.map((c, i) => (
            <div key={i} className="grid gap-2 sm:grid-cols-[11rem_minmax(0,1fr)_auto] sm:items-start">
              <Input
                aria-label={`Nom du champ ${i + 1}`}
                value={c.nom}
                className="font-mono text-ui"
                onChange={(e) => maj({ champs: b.champs.map((x, j) => (j === i ? { ...x, nom: e.target.value } : x)) })}
              />
              <EditeurGabarit
                aria-label={`Valeur de ${c.nom || `champ ${i + 1}`}`}
                value={c.valeur}
                onChange={(v) => maj({ champs: b.champs.map((x, j) => (j === i ? { ...x, valeur: v } : x)) })}
                format="Texte"
                catalogue={catalogue}
                variables={variables}
                uneLigne
                rows={1}
                onFocus={() => surChamp(`champ:${i}`, null)}
                ref={(el) => surChamp(`champ:${i}`, el)}
              />
              <BoutonIcone
                label={`Retirer le champ ${c.nom || i + 1}`}
                taille="ic-md"
                onClick={() => maj({ champs: b.champs.filter((_, j) => j !== i) })}
              >
                <Trash2 className="size-4" aria-hidden />
              </BoutonIcone>
            </div>
          ))}
          <div>
            <Button
              taille="sm"
              variante="fantome"
              icone={<Plus className="size-4" aria-hidden />}
              onClick={() => maj({ champs: [...b.champs, { nom: '', valeur: '' }] })}
            >
              Ajouter un champ
            </Button>
          </div>
          <p className="text-xs text-fg-subtle">Envoyé en application/x-www-form-urlencoded, valeurs encodées.</p>
        </div>
      )}

      {b.format === 'Aucun' && <p className="text-sm text-fg-subtle">La requête part sans message.</p>}

      {inconnues.map((c) => {
        const s = suggestion(
          c,
          variables.map((v) => v.cle),
        );
        return (
          <Alerte
            key={c}
            ton="danger"
            icone={OctagonAlert}
            role="alert"
            action={
              s && (
                <Button taille="sm" onClick={() => corriger(c, s)}>
                  Corriger
                </Button>
              )
            }
          >
            Variable inconnue : <code className="font-mono">{`{{${c}}}`}</code>.
            {s ? (
              <>
                {' '}
                Vouliez-vous dire <code className="font-mono">{`{{${s}}}`}</code> ({libelles.get(s)}) ?
              </>
            ) : (
              ' Choisissez-la dans « Insérer une variable ».'
            )}
          </Alerte>
        );
      })}

      {b.format !== 'Aucun' && (
        <section aria-label="Insérer une variable" className="rounded-lg border border-line bg-surface-2/50 p-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="flex items-center gap-2 text-ui font-medium">
              <Braces className="size-4 text-accent" aria-hidden />
              Insérer une variable
              <span className="font-normal text-fg-subtle">: ou tapez {'{{'} dans le message.</span>
            </p>
            <div className="relative w-full sm:w-64">
              <Search
                className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
                aria-hidden
              />
              <Input
                aria-label="Rechercher une variable"
                placeholder="Rechercher une variable…"
                className="h-8 pl-9"
                value={recherche}
                onChange={(e) => setRecherche(e.target.value)}
              />
            </div>
          </div>
          <div className="mt-3 grid max-h-96 gap-y-2.5 overflow-y-auto">
            {groupes.map((g) => (
              <div key={g.groupe} className="grid gap-2 sm:grid-cols-[9rem_minmax(0,1fr)]">
                <p className="pt-1 text-2xs font-semibold tracking-wider text-fg-subtle uppercase">{g.groupe}</p>
                <div className="flex flex-wrap gap-1.5">
                  {g.variables.map((v) => (
                    <Tooltip key={v.cle} content={`{{${v.cle}}} : ${v.description}`}>
                      <button
                        type="button"
                        onMouseDown={(e) => e.preventDefault()}
                        onClick={() => inserer(v.cle)}
                        className="rounded-md border border-line bg-surface px-2 py-0.5 text-xs text-fg-muted transition-colors duration-150 hover:border-accent hover:text-accent-text"
                      >
                        {v.libelle}
                      </button>
                    </Tooltip>
                  ))}
                </div>
              </div>
            ))}
            {groupes.length === 0 && (
              <p className="text-sm text-fg-subtle">Aucune variable ne correspond à « {recherche} ».</p>
            )}
          </div>
        </section>
      )}
    </Card>
  );
}
