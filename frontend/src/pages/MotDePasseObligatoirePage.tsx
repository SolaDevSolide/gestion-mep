import { LogOut, Rocket, ShieldAlert } from 'lucide-react';
import { useAuth } from '../auth';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { FormulaireMotDePasse } from './FormulaireMotDePasse';

/** Page plein écran affichée tant que le mot de passe provisoire n'a pas été remplacé. */
export default function MotDePasseObligatoirePage() {
  const { utilisateur, deconnecter } = useAuth();
  return (
    <main className="flex min-h-[calc(100vh-var(--bandeau-demo,0px))] items-center justify-center bg-bg p-6">
      <div className="w-full max-w-md">
        <div className="mb-8 flex items-center gap-3">
          <span className="flex size-10 items-center justify-center rounded-xl bg-accent text-accent-fg shadow-sm">
            <Rocket className="size-5" aria-hidden />
          </span>
          <span className="text-lg font-semibold tracking-tight">Gestion MEP</span>
        </div>
        <h1 className="text-2xl font-semibold tracking-tight">Choisissez votre mot de passe</h1>
        <p className="mt-1 mb-5 text-sm text-fg-subtle">
          Bonjour {utilisateur?.nomAffiche}. Votre mot de passe est provisoire : remplacez-le pour accéder à
          l’application.
        </p>
        <Alerte ton="warn" icone={ShieldAlert} className="mb-5">
          Saisissez le mot de passe provisoire qui vous a été transmis, puis votre nouveau mot de passe.
        </Alerte>
        <FormulaireMotDePasse
          libelleAncien="Mot de passe provisoire"
          pied={
            <Button
              variante="fantome"
              icone={<LogOut className="size-4" aria-hidden />}
              onClick={() => void deconnecter()}
            >
              Se déconnecter
            </Button>
          }
        />
      </div>
    </main>
  );
}
