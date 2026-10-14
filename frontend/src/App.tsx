import { Loader2 } from 'lucide-react';
import { lazy } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth';
import { BandeauDemo } from './layout/BandeauDemo';
import { GardeModifications } from './layout/GardeModifications';
import { NouvelleVersion } from './layout/NouvelleVersion';
import { Shell } from './layout/Shell';
import ConnexionPage from './pages/ConnexionPage';
import MotDePasseObligatoirePage from './pages/MotDePasseObligatoirePage';

// Pages chargées à la demande : l'écran de connexion n'attend pas le code de l'éditeur, de l'exécution ou de la console.
const AdminPage = lazy(() => import('./admin/AdminPage'));
const ListeMepPage = lazy(() => import('./pages/ListeMepPage'));
const ProfilPage = lazy(() => import('./pages/ProfilPage'));
const MepPage = lazy(() => import('./pages/MepPage'));
const GoNoGoPage = lazy(() => import('./parametres/GoNoGoPage'));
const NouvelleResolutionPage = lazy(() => import('./parametres/NouvelleResolutionPage'));
const ResolutionsPage = lazy(() => import('./parametres/ResolutionsPage'));
const SystemesPage = lazy(() => import('./parametres/SystemesPage'));

function Chargement() {
  return (
    <div
      className="flex min-h-[calc(100vh-var(--bandeau-demo,0px))] items-center justify-center text-fg-subtle"
      role="status"
    >
      <Loader2 className="mr-2 size-5 animate-spin" aria-hidden /> Chargement…
    </div>
  );
}

function Protege() {
  const { utilisateur, chargement } = useAuth();
  if (chargement) return <Chargement />;
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
          <Route path="/meps/:id" element={<MepPage />} />
          <Route path="/profil" element={<ProfilPage />} />
          <Route path="/admin" element={<AdminPage />} />
          <Route path="/parametres/systemes" element={<SystemesPage />} />
          <Route path="/parametres/resolutions" element={<ResolutionsPage />} />
          <Route path="/parametres/resolutions/nouvelle" element={<NouvelleResolutionPage />} />
          <Route path="/parametres/go-no-go" element={<GoNoGoPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </>
  );
}
