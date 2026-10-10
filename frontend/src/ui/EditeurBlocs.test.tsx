import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { EditeurBlocs } from './EditeurBlocs';

function Hote({ initial, onChange }: { initial: string; onChange: (v: string) => void }) {
  const [v, setV] = useState(initial);
  return <EditeurBlocs label="Instructions" valeur={v} onChange={(x) => { setV(x); onChange(x); }} />;
}

describe('EditeurBlocs', () => {
  it('découpe la valeur en blocs Texte et Code, avec aperçu markdown du texte', () => {
    render(<Hote initial={'Faire la **sauvegarde**.\n\n```\npg_dump clients\n```'} onChange={vi.fn()} />);
    expect(screen.getByLabelText('Instructions, bloc 1 (texte)')).toHaveValue('Faire la **sauvegarde**.');
    expect(screen.getByLabelText('Instructions, bloc 2 (code)')).toHaveValue('pg_dump clients');
    expect(screen.getByText('sauvegarde', { selector: 'strong' })).toBeInTheDocument();
  });

  it('valeur vide : un bloc texte vide, rien n’est émis', () => {
    const onChange = vi.fn();
    render(<Hote initial="" onChange={onChange} />);
    expect(screen.getByLabelText('Instructions, bloc 1 (texte)')).toHaveValue('');
    expect(onChange).not.toHaveBeenCalled();
  });

  it('ajoute un bloc code et recompose la chaîne markdown', async () => {
    const onChange = vi.fn();
    render(<Hote initial="Prévenir le support." onChange={onChange} />);
    await userEvent.click(screen.getByRole('button', { name: 'Bloc code' }));
    await userEvent.type(screen.getByLabelText('Instructions, bloc 2 (code)'), './deploy.sh');
    expect(onChange).toHaveBeenLastCalledWith('Prévenir le support.\n\n```\n./deploy.sh\n```');
  });

  it('bascule un bloc de Texte à Code', async () => {
    const onChange = vi.fn();
    render(<Hote initial="systemctl restart api" onChange={onChange} />);
    await userEvent.click(screen.getByRole('radio', { name: 'Code' }));
    expect(onChange).toHaveBeenLastCalledWith('```\nsystemctl restart api\n```');
    expect(screen.getByLabelText('Instructions, bloc 1 (code)')).toBeInTheDocument();
  });

  it('monte, descend et supprime les blocs', async () => {
    const onChange = vi.fn();
    render(<Hote initial={'A\n\n```\nB\n```'} onChange={onChange} />);
    await userEvent.click(screen.getByRole('button', { name: 'Monter le bloc 2' }));
    expect(onChange).toHaveBeenLastCalledWith('```\nB\n```\n\nA');
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer le bloc 1' }));
    expect(onChange).toHaveBeenLastCalledWith('A');
  });

  it('suit une valeur remplacée de l’extérieur (annulation)', () => {
    const { rerender } = render(<EditeurBlocs label="Instructions" valeur="avant" onChange={vi.fn()} />);
    rerender(<EditeurBlocs label="Instructions" valeur={'```\naprès\n```'} onChange={vi.fn()} />);
    expect(screen.getByLabelText('Instructions, bloc 1 (code)')).toHaveValue('après');
  });
});
