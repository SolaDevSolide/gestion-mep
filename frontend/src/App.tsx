import { Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth';
import { EnTete } from './components/EnTete';
import ConnexionPage from './pages/ConnexionPage';
import ListeMepPage from './pages/ListeMepPage';
import MepPage from './pages/MepPage';

function Protege() {
  const { utilisateur, chargement } = useAuth();
  if (chargement) return <p className="chargement">Chargement…</p>;
  if (!utilisateur) return <Navigate to="/connexion" replace />;
  return (
    <>
      <EnTete />
      <Outlet />
    </>
  );
}

export default function App() {
  return (
    <Routes>
      <Route path="/connexion" element={<ConnexionPage />} />
      <Route element={<Protege />}>
        <Route path="/" element={<ListeMepPage />} />
        <Route path="/meps/:id" element={<MepPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
