import { ArrowDown, ArrowUp, ClipboardCheck, Pencil, Plus, Trash2, TriangleAlert } from 'lucide-react';
import { useMemo, useState } from 'react';
import { api } from '../../api';
import { nomParticipant, testsRecetteTries } from '../../domaine';
import { useMep } from '../../mepContext';
import type { TestRecette } from '../../types';
import { Avatar } from '../../ui/Avatar';
import { BoutonIcone, Button } from '../../ui/Button';
import { ConfirmDialog } from '../../ui/Dialog';
import { EmptyState } from '../../ui/EmptyState';
import { Tooltip } from '../../ui/Tooltip';
import { FormulaireDialog, SectionCarte, vide, type ChampForm } from './Formulaires';

/** Tests de recette à réaliser après le déploiement : ajout, modification, ordre, suppression. */
export function RecetteSection() {
  const { mep, run } = useMep();
  const edition = mep.droits.peutModifierPlan;
  const tests = testsRecetteTries(mep);
  const [forme, setForme] = useState<{ existant?: TestRecette } | null>(null);
  const [suppr, setSuppr] = useState<TestRecette | null>(null);

  const champs: ChampForm[] = [
    { cle: 'libelle', label: 'Test à réaliser', requis: true, large: true, placeholder: 'ex. Ouvrir la page d’accueil' },
    { cle: 'resultatAttendu', label: 'Résultat attendu', type: 'zone', requis: true, placeholder: 'ex. La page s’affiche, la version 4.2 est visible.' },
    {
      cle: 'responsableId', label: 'Responsable (relié à un compte)', type: 'select',
      options: [{ valeur: '', libelle: '— choisir —' }, ...mep.participants.map((p) => ({ valeur: p.id, libelle: p.utilisateurNom ? p.nom : `${p.nom} (sans compte)` }))],
    },
  ];
  const initial = useMemo(() => ({
    libelle: forme?.existant?.libelle ?? '', resultatAttendu: forme?.existant?.resultatAttendu ?? '', responsableId: forme?.existant?.responsableId ?? '',
  }), [forme]);

  return (
    <SectionCarte
      numero={6}
      titre="Recette"
      description="Les tests à réaliser en production une fois le déploiement terminé. La clôture réussie exige que tous soient conformes."
      action={edition && <Button variante="primaire" taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => setForme({})}>Ajouter un test</Button>}
    >
      {tests.length === 0 ? (
        <EmptyState icone={ClipboardCheck} titre="Aucun test de recette" description="Ajoutez au moins un test : ce qu’on vérifie, le résultat attendu et qui le fait." />
      ) : (
        <ol className="divide-y divide-line overflow-hidden rounded-lg border border-line">
          {tests.map((t, i) => {
            const resp = nomParticipant(mep, t.responsableId);
            const sansCompte = t.responsableId !== null && !mep.participants.find((p) => p.id === t.responsableId)?.utilisateurId;
            return (
              <li key={t.id} className="flex flex-wrap items-start gap-x-4 gap-y-2 px-4 py-3">
                <span className="mt-0.5 flex size-6 shrink-0 items-center justify-center rounded-md bg-surface-2 font-mono text-xs font-bold">{i + 1}</span>
                <div className="min-w-0 flex-1 basis-64">
                  <p className="font-medium">{t.libelle || <em className="text-fg-subtle not-italic">Test à décrire</em>}</p>
                  <p className="mt-0.5 text-sm whitespace-pre-line text-fg-muted">{t.resultatAttendu || 'Résultat attendu à décrire'}</p>
                </div>
                <div className="flex items-center gap-2 text-sm text-fg-muted">
                  {resp ? <><Avatar nom={resp} taille="sm" />{resp}</> : <span className="text-fg-subtle">Sans responsable</span>}
                  {sansCompte && (
                    <Tooltip content="Ce responsable n’est relié à aucun compte : il ne pourra pas saisir le résultat."><span className="text-warn-solid"><TriangleAlert className="size-4" aria-label="Responsable sans compte" /></span></Tooltip>
                  )}
                </div>
                {edition && (
                  <div className="ml-auto flex items-center gap-0.5">
                    <BoutonIcone label={`Monter le test ${i + 1}`} taille="sm" disabled={i === 0} onClick={() => void run(() => api.deplacerTestRecette(mep.id, t.id, 'haut'))}><ArrowUp className="size-4" aria-hidden /></BoutonIcone>
                    <BoutonIcone label={`Descendre le test ${i + 1}`} taille="sm" disabled={i === tests.length - 1} onClick={() => void run(() => api.deplacerTestRecette(mep.id, t.id, 'bas'))}><ArrowDown className="size-4" aria-hidden /></BoutonIcone>
                    <BoutonIcone label={`Modifier le test ${i + 1}`} taille="sm" onClick={() => setForme({ existant: t })}><Pencil className="size-4" aria-hidden /></BoutonIcone>
                    <BoutonIcone label={`Supprimer le test ${i + 1}`} taille="sm" onClick={() => setSuppr(t)}><Trash2 className="size-4" aria-hidden /></BoutonIcone>
                  </div>
                )}
              </li>
            );
          })}
        </ol>
      )}
      <FormulaireDialog
        open={forme !== null}
        onOpenChange={(o) => { if (!o) setForme(null); }}
        titre={forme?.existant ? 'Modifier le test' : 'Ajouter un test de recette'}
        champs={champs}
        initial={initial}
        libelleValider={forme?.existant ? 'Enregistrer' : 'Ajouter'}
        onValider={(v) => {
          const s = { libelle: v.libelle.trim(), resultatAttendu: v.resultatAttendu.trim(), responsableId: vide(v.responsableId) };
          const ex = forme?.existant;
          return run(() => (ex ? api.modifierTestRecette(mep.id, ex.id, s) : api.ajouterTestRecette(mep.id, s)), ex ? 'Test modifié.' : 'Test ajouté.');
        }}
      />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => { if (!o) setSuppr(null); }}
        titre="Supprimer ce test ?"
        description={suppr ? `« ${suppr.libelle} » sera retiré de la recette.` : undefined}
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={() => suppr ? run(() => api.supprimerTestRecette(mep.id, suppr.id), 'Test supprimé.') : false}
      />
    </SectionCarte>
  );
}

