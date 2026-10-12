import { OctagonAlert, RefreshCw } from 'lucide-react';
import { Component, type ErrorInfo, type ReactNode } from 'react';
import { Alerte } from './Alerte';
import { Button } from './Button';

/**
 * Filet de sécurité : une erreur pendant l'affichage d'une page n'efface plus tout l'écran (React démonte sinon
 * l'application entière). Le menu reste utilisable, la page affiche l'erreur et propose de recharger. Changer de
 * page ou d'onglet (`cle`) réessaie l'affichage.
 */
export class FiletErreur extends Component<
  { cle: string; children: ReactNode },
  { erreur: Error | null; cle: string }
> {
  state = { erreur: null as Error | null, cle: this.props.cle };

  static getDerivedStateFromError(erreur: Error) {
    return { erreur };
  }

  static getDerivedStateFromProps(props: { cle: string }, state: { erreur: Error | null; cle: string }) {
    return props.cle === state.cle ? null : { erreur: null, cle: props.cle };
  }

  componentDidCatch(erreur: Error, info: ErrorInfo) {
    console.error('Erreur d’affichage interceptée', erreur, info.componentStack);
  }

  render() {
    const { erreur } = this.state;
    if (!erreur) return this.props.children;
    return (
      <div className="mx-auto w-full max-w-2xl px-6 py-16">
        <Alerte
          ton="danger"
          icone={OctagonAlert}
          role="alert"
          titre="Cette page n’a pas pu s’afficher"
          action={
            <Button icone={<RefreshCw className="size-4" aria-hidden />} onClick={() => window.location.reload()}>
              Recharger la page
            </Button>
          }
        >
          {`Le reste de l’application reste utilisable depuis le menu. Si l’erreur revient, signalez-la avec ce message :\n${erreur.message}`}
        </Alerte>
      </div>
    );
  }
}
