import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BoutonIcone, Button } from './Button';

function promesse() {
  let resoudre!: () => void;
  const p = new Promise<void>((r) => (resoudre = r));
  return { p, resoudre };
}

describe('Button : action', () => {
  it('occupe le bouton tant que la promesse de l’action court, sans relancer au second clic', async () => {
    const { p, resoudre } = promesse();
    const action = vi.fn(() => p);
    const onClick = vi.fn();
    render(
      <Button action={action} onClick={onClick}>
        Enregistrer
      </Button>,
    );
    const bouton = screen.getByRole('button', { name: 'Enregistrer' });
    await userEvent.click(bouton);
    expect(onClick).toHaveBeenCalledOnce();
    expect(action).toHaveBeenCalledOnce();
    expect(bouton).toBeDisabled();
    expect(bouton).toHaveAttribute('aria-busy', 'true');
    await userEvent.click(bouton);
    expect(action).toHaveBeenCalledOnce();
    await act(async () => {
      resoudre();
      await p;
    });
    expect(bouton).toBeEnabled();
    expect(bouton).not.toHaveAttribute('aria-busy');
  });

  it('se libère aussi quand l’action échoue', async () => {
    let rejeter!: (e: Error) => void;
    const p = new Promise<void>((_, r) => (rejeter = r));
    render(<Button action={() => p}>Valider</Button>);
    const bouton = screen.getByRole('button', { name: 'Valider' });
    await userEvent.click(bouton);
    expect(bouton).toBeDisabled();
    await act(async () => {
      rejeter(new Error('non'));
      await p.catch(() => undefined);
    });
    expect(bouton).toBeEnabled();
  });

  it('action synchrone : le bouton ne passe pas en attente', async () => {
    const action = vi.fn();
    render(<Button action={action}>Ajouter</Button>);
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter' }));
    expect(action).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: 'Ajouter' })).toBeEnabled();
  });

  it('bouton icône occupé : la roue remplace l’icône, le libellé accessible reste', async () => {
    const { p, resoudre } = promesse();
    render(
      <BoutonIcone label="Retirer" action={() => p}>
        <svg data-testid="icone" />
      </BoutonIcone>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Retirer' }));
    expect(screen.queryByTestId('icone')).toBeNull();
    expect(screen.getByRole('button', { name: 'Retirer' })).toHaveAttribute('aria-busy', 'true');
    await act(async () => {
      resoudre();
      await p;
    });
    expect(screen.getByTestId('icone')).toBeInTheDocument();
  });
});
