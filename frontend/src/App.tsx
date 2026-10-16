import { lazy } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { api } from './api';
import { useAuth } from './auth';
import { useAsync } from './hooks';
import { BandeauDemo } from './layout/BandeauDemo';
import { GardeModifications } from './layout/GardeModifications';
import { NouvelleVersion } from './layout/NouvelleVersion';
import { Shell } from './layout/Shell';
import ConnexionPage from './pages/ConnexionPage';
import MotDePasseObligatoirePage from './pages/MotDePasseObligatoirePage';
import { EnChargement, Squelette, SqueletteCartes, SqueletteLignes } from './ui/Squelette';

// Pages chargées à la demande : l'écran de connexion n'attend pas le code de l'éditeur, de l'exécution ou de la console.
const AdminPage = lazy(() => import('./admin/AdminPage'));
const ListeMepPage = lazy(() => import('./pages/ListeMepPage'));
const ProfilPage = lazy(() => import('./pages/ProfilPage'));
const MepPage = lazy(() => import('./pages/MepPage'));
const SuiviPage = lazy(() => import('./pages/SuiviPage'));
const RapportPage = lazy(() => import('./pages/RapportPage'));
const GoNoGoPage = lazy(() => import('./parametres/GoNoGoPage'));
const NouvelleResolutionPage = lazy(() => import('./parametres/NouvelleResolutionPage'));
const ResolutionsPage = lazy(() => import('./parametres/ResolutionsPage'));
const BibliothequeEtapesPage = lazy(() => import('./parametres/BibliothequeEtapesPage'));
const BibliothequeRecettesPage = lazy(() => import('./parametres/BibliothequeRecettesPage'));
const SystemesPage = lazy(() => import('./parametres/SystemesPage'));
const ModelesTextePage = lazy(() => import('./parametres/ModelesTextePage'));
const ModeleTexteEditeurPage = lazy(() => import('./parametres/ModeleTexteEditeurPage'));
const SystemeFichePage = lazy(() => import('./parametres/SystemeFichePage'));

/**
 * Premier affichage (session et mode démonstration en cours de lecture) : la silhouette de l'application, menu compris,
 * plutôt qu'un écran vide ; le menu et la page prennent ensuite sa place sans saut.
 */
function Chargement() {
  return (
    <EnChargement libelle="Chargement de l’application…" className="flex min-h-[calc(100vh-var(--bandeau-demo,0px))]">
      <div className="flex w-64 shrink-0 flex-col gap-3 border-r border-line bg-surface px-4 py-3">
        <div className="flex h-8 items-center gap-2.5">
          <Squelette className="size-8 rounded-lg" />
          <Squelette className="h-4 w-24" />
        </div>
        <Squelette className="h-9 w-full rounded-lg" />
        <SqueletteLignes lignes={8} className="mt-3 gap-4" />
      </div>
      <div className="mx-auto w-full max-w-6xl px-6 py-8">
        <Squelette className="h-7 w-72" />
        <Squelette className="mt-2 h-4 w-96" />
        <SqueletteCartes cartes={3} hauteur="h-28" className="mt-8 sm:grid-cols-1 xl:grid-cols-1" />
      </div>
    </EnChargement>
  );
}

function Protege() {
  const { utilisateur, chargement } = useAuth();
  // Le bandeau de démonstration paraît en même temps que le reste (pas de page qui descend d'un cran).
  const demo = useAsync(() => api.demo(), []);
  if (chargement || demo.chargement) return <Chargement />;
  if (!utilisateur) return <Navigate to="/connexion" replace />;
  if (utilisateur.doitChangerMotDePasse) return <MotDePasseObligatoirePage />;
  return (
    <GardeModifications>
      <Shell />
    </GardeModifications>
  );
}

export default function App() {
  return (
    <>
      <NouvelleVersion />
      <BandeauDemo />
      <Routes>
        <Route path="/connexion" element={<ConnexionPage />} />
        <Route element={<Protege />}>
          <Route path="/" element={<ListeMepPage />} />
          <Route path="/suivi" element={<SuiviPage />} />
          <Route path="/meps/:id" element={<MepPage />} />
          <Route path="/meps/:id/rapport" element={<RapportPage />} />
          <Route path="/profil" element={<ProfilPage />} />
          <Route path="/admin" element={<AdminPage />} />
          <Route path="/parametres/systemes" element={<SystemesPage />} />
          <Route path="/parametres/systemes/:id" element={<SystemeFichePage />} />
          <Route path="/parametres/resolutions" element={<ResolutionsPage />} />
          <Route path="/parametres/resolutions/nouvelle" element={<NouvelleResolutionPage />} />
          <Route path="/parametres/etapes" element={<BibliothequeEtapesPage />} />
          <Route path="/parametres/recettes" element={<BibliothequeRecettesPage />} />
          <Route path="/parametres/go-no-go" element={<GoNoGoPage />} />
          <Route path="/parametres/textes" element={<ModelesTextePage />} />
          <Route path="/parametres/textes/:id" element={<ModeleTexteEditeurPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </>
  );
}
