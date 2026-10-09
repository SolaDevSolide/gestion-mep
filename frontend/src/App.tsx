import { Loader2 } from 'lucide-react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth';
import { Shell } from './layout/Shell';
import ConnexionPage from './pages/ConnexionPage';
import ListeMepPage from './pages/ListeMepPage';
import MepPage from './pages/MepPage';
import GoNoGoPage from './parametres/GoNoGoPage';
import ResolutionsPage from './parametres/ResolutionsPage';
import SystemesPage from './parametres/SystemesPage';

function Protege() {
  const { utilisateur, chargement } = useAuth();
  if (chargement) {
    return (
      <div className="flex min-h-screen items-center justify-center text-fg-subtle" role="status">
        <Loader2 className="mr-2 size-5 animate-spin" aria-hidden /> Chargement…
      </div>
    );
  }
  if (!utilisateur) return <Navigate to="/connexion" replace />;
  return <Shell />;
}

export default function App() {
  return (
    <Routes>
      <Route path="/connexion" element={<ConnexionPage />} />
      <Route element={<Protege />}>
        <Route path="/" element={<ListeMepPage />} />
        <Route path="/meps/:id" element={<MepPage />} />
        <Route path="/parametres/systemes" element={<SystemesPage />} />
        <Route path="/parametres/resolutions" element={<ResolutionsPage />} />
        <Route path="/parametres/go-no-go" element={<GoNoGoPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
