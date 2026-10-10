import { History, ListOrdered, OctagonAlert, Pencil, Plus, Search, Trash2, Wrench } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync } from '../hooks';
import { correspond, termes } from '../recherche';
import { ResolutionEditeurDialog } from '../mep/ResolutionEditeur';
import type { ResolutionType } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { ConfirmDialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Input, Select } from '../ui/Input';
import { Badge } from '../ui/StatusPill';
import { Markdown } from '../ui/Instructions';
import { Tooltip } from '../ui/Tooltip';
import { auteurModification, HistoriqueResolutionDialog } from './HistoriqueResolution';
import { useToast } from '../ui/Toast';

export default function ResolutionsPage() {
  const { utilisateur } = useAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const admin = utilisateur?.estAdmin ?? false;
  const res = useAsync(() => api.resolutions(), []);
  const cat = useAsync(() => api.catalogueSystemes(), []);
  const [recherche, setRecherche] = useState('');
  const [systeme, setSysteme] = useState('');
  const [edition, setEdition] = useState<{ existant?: ResolutionType } | null>(null);
  const [suppr, setSuppr] = useState<ResolutionType | null>(null);
  const [historique, setHistorique] = useState<ResolutionType | null>(null);

  const noms = useMemo(() => new Map((cat.donnees ?? []).map((s) => [s.id, s.nom])), [cat.donnees]);
  const filtrees = useMemo(() => {
    const ts = termes(recherche);
    return (res.donnees ?? []).filter(
      (r) =>
        (!systeme ||
          (systeme === 'aucun' ? r.systemeCatalogueIds.length === 0 : r.systemeCatalogueIds.includes(systeme))) &&
        correspond(
          {
            titre: r.titre,
            textes: [r.symptomes, r.diagnostic, ...r.systemeCatalogueIds.map((s) => noms.get(s) ?? '')],
          },
          ts,
        ),
    );
  }, [res.donnees, recherche, systeme, noms]);

  return (
    <main className="mx-auto w-full max-w-6xl px-6 py-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Résolutions types</h1>
        </div>
        <Button
          variante="primaire"
          icone={<Plus className="size-4" aria-hidden />}
          onClick={() => void navigate('/parametres/resolutions/nouvelle')}
        >
          Nouvelle résolution
        </Button>
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-3">
        <div className="relative min-w-60 flex-1 sm:max-w-sm">
          <Search
            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
            aria-hidden
          />
          <Input
            aria-label="Rechercher une résolution"
            placeholder="Rechercher (titre, symptômes)…"
            className="pl-9"
            value={recherche}
            onChange={(e) => setRecherche(e.target.value)}
          />
        </div>
        <div className="w-56">
          <Select aria-label="Filtrer par système" value={systeme} onChange={(e) => setSysteme(e.target.value)}>
            <option value="">Tous les systèmes</option>
            <option value="aucun">Sans système précis</option>
            {(cat.donnees ?? []).map((s) => (
              <option key={s.id} value={s.id}>
                {s.nom}
              </option>
            ))}
          </Select>
        </div>
      </div>

      {res.erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">
          {res.erreur}
        </Alerte>
      )}

      {res.donnees !== null && filtrees.length === 0 && (
        <Card className="mt-6">
          <EmptyState
            icone={Wrench}
            titre={res.donnees.length === 0 ? 'Aucune résolution type' : 'Aucun résultat'}
            description={
              res.donnees.length === 0
                ? 'Écrivez une première procédure : symptômes, corrections ordonnées et vérification finale.'
                : 'Aucune résolution ne correspond à ces critères.'
            }
            action={
              res.donnees.length === 0 ? (
                <Button
                  variante="primaire"
                  icone={<Plus className="size-4" aria-hidden />}
                  onClick={() => void navigate('/parametres/resolutions/nouvelle')}
                >
                  Nouvelle résolution
                </Button>
              ) : undefined
            }
          />
        </Card>
      )}

      <ul className="mt-6 grid gap-3 lg:grid-cols-2">
        {filtrees.map((r) => (
          <li key={r.id}>
            <Card className="flex h-full flex-col p-4 transition-shadow duration-150 hover:shadow-md">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <h2 className="flex items-center gap-2 font-semibold">
                    <Wrench className="size-4 shrink-0 text-warn-solid" aria-hidden />
                    <span className="truncate">{r.titre}</span>
                  </h2>
                  <div className="mt-1.5 flex flex-wrap items-center gap-2">
                    {r.systemeCatalogueIds.length ? (
                      r.systemeCatalogueIds.map((s) => <Badge key={s}>{noms.get(s) ?? 'Système'}</Badge>)
                    ) : (
                      <Badge>Tous les systèmes</Badge>
                    )}
                    <Badge>
                      <ListOrdered className="size-3" aria-hidden />
                      {r.corrections.length} correction{r.corrections.length > 1 ? 's' : ''}
                    </Badge>
                  </div>
                </div>
                <div className="flex shrink-0 gap-0.5">
                  <Tooltip content="Historique">
                    <BoutonIcone label={`Historique de ${r.titre}`} taille="sm" onClick={() => setHistorique(r)}>
                      <History className="size-4" aria-hidden />
                    </BoutonIcone>
                  </Tooltip>
                  <Tooltip content="Modifier">
                    <BoutonIcone label={`Modifier ${r.titre}`} taille="sm" onClick={() => setEdition({ existant: r })}>
                      <Pencil className="size-4" aria-hidden />
                    </BoutonIcone>
                  </Tooltip>
                  {admin && (
                    <Tooltip content="Supprimer">
                      <BoutonIcone label={`Supprimer ${r.titre}`} taille="sm" onClick={() => setSuppr(r)}>
                        <Trash2 className="size-4" aria-hidden />
                      </BoutonIcone>
                    </Tooltip>
                  )}
                </div>
              </div>
              {r.symptomes ? (
                <Markdown className="mt-3 line-clamp-3 text-fg-muted" source={r.symptomes} />
              ) : (
                <p className="mt-3 text-sm text-fg-muted">Symptômes non décrits.</p>
              )}
              <p className="mt-auto pt-3 text-xs text-fg-subtle">
                {auteurModification(r)} · version {r.version}
              </p>
            </Card>
          </li>
        ))}
      </ul>

      <ResolutionEditeurDialog
        key={edition?.existant?.id ?? 'nouvelle'}
        open={edition !== null}
        onOpenChange={(o) => {
          if (!o) setEdition(null);
        }}
        initial={edition?.existant}
        onEnregistree={() => res.recharger()}
      />
      <HistoriqueResolutionDialog resolution={historique} onFermer={() => setHistorique(null)} />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => {
          if (!o) setSuppr(null);
        }}
        titre={`Supprimer « ${suppr?.titre ?? ''} » ?`}
        description="Si elle est liée à une étape, elle sera seulement désactivée."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={async () => {
          if (!suppr) return false;
          try {
            await api.supprimerResolution(suppr.id);
            toast.succes('Résolution supprimée ou désactivée.');
            res.recharger();
            return true;
          } catch (e) {
            toast.erreur(e instanceof Error ? e.message : 'Suppression impossible.');
            return false;
          }
        }}
      />
    </main>
  );
}
