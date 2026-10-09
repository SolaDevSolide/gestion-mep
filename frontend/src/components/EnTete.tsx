import { Link } from 'react-router-dom';
import { useAuth } from '../auth';

export function EnTete() {
  const { utilisateur, deconnecter } = useAuth();
  return (
    <header className="entete">
      <Link to="/" className="entete-titre">Gestion des MEP</Link>
      <div className="entete-user">
        <span>{utilisateur?.nomAffiche}</span>
        <button type="button" className="btn btn-secondaire btn-petit" onClick={() => void deconnecter()}>
          Déconnexion
        </button>
      </div>
    </header>
  );
}
