import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { EditeurBlocs } from './EditeurBlocs';

function Hote({
  initial,
  onChange,
  disposition,
}: {
  initial: string;
  onChange: (v: string) => void;
  disposition?: 'onglets' | 'cote';
}) {
  const [v, setV] = useState(initial);
  return (
    <EditeurBlocs
      label="Instructions"
      valeur={v}
      disposition={disposition}
      onChange={(x) => {
        setV(x);
        onChange(x);
      }}
    />
  );
}

describe('EditeurBlocs', () => {
  it('découpe la valeur en blocs Texte et Code ; l’onglet Aperçu rend le markdown', async () => {
    render(<Hote initial={'Faire la **sauvegarde**.\n\n```\npg_dump clients\n```'} onChange={vi.fn()} />);
    expect(screen.getByLabelText('Instructions, bloc 1 (texte)')).toHaveValue('Faire la **sauvegarde**.');
    expect(screen.getByLabelText('Instructions, bloc 2 (code)')).toHaveValue('pg_dump clients');
    await userEvent.click(screen.getByRole('tab', { name: 'Aperçu' }));
    expect(screen.getByText('sauvegarde', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Instructions, bloc 1 (texte)')).toBeNull();
  });

  it('côte à côte : saisie et aperçu visibles ensemble', () => {
    render(<Hote initial="**ok**" onChange={vi.fn()} disposition="cote" />);
    expect(screen.getByLabelText('Instructions, bloc 1 (texte)')).toBeInTheDocument();
    expect(screen.getByText('ok', { selector: 'strong' })).toBeInTheDocument();
  });

  it('valeur vide : un bloc vide avec exemple, rien n’est émis', () => {
    const onChange = vi.fn();
    render(<Hote initial="" onChange={onChange} />);
    expect(screen.getByLabelText('Instructions, bloc 1 (texte)')).toHaveValue('');
    expect(screen.getByLabelText('Instructions, bloc 1 (texte)')).toHaveAttribute(
      'placeholder',
      expect.stringContaining('Décrivez l’action'),
    );
    expect(onChange).not.toHaveBeenCalled();
  });

  it('ajoute un bloc code en bas et recompose la chaîne markdown', async () => {
    const onChange = vi.fn();
    render(<Hote initial="Prévenir le support." onChange={onChange} />);
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter un bloc code' }));
    expect(screen.getByLabelText('Instructions, bloc 2 (code)')).toHaveFocus();
    await userEvent.type(screen.getByLabelText('Instructions, bloc 2 (code)'), './deploy.sh');
    expect(onChange).toHaveBeenLastCalledWith('Prévenir le support.\n\n```\n./deploy.sh\n```');
  });

  it('insère un bloc entre deux blocs', async () => {
    const onChange = vi.fn();
    render(<Hote initial={'A\n\n```\nB\n```'} onChange={onChange} />);
    await userEvent.click(screen.getByRole('button', { name: 'Insérer un bloc après le bloc 1' }));
    await userEvent.click(screen.getByRole('button', { name: 'Insérer un bloc code après le bloc 1' }));
    await userEvent.type(screen.getByLabelText('Instructions, bloc 2 (code)'), 'ls');
    expect(onChange).toHaveBeenLastCalledWith('A\n\n```\nls\n```\n\n```\nB\n```');
  });

  it('bascule un bloc de Texte à Code', async () => {
    const onChange = vi.fn();
    render(<Hote initial="systemctl restart api" onChange={onChange} />);
    await userEvent.click(screen.getByRole('radio', { name: 'Code' }));
    expect(onChange).toHaveBeenLastCalledWith('```\nsystemctl restart api\n```');
    expect(screen.getByLabelText('Instructions, bloc 1 (code)')).toBeInTheDocument();
  });

  it('barre d’outils : met la sélection en gras', async () => {
    const onChange = vi.fn();
    render(<Hote initial="faire la sauvegarde" onChange={onChange} />);
    const zone = screen.getByLabelText<HTMLTextAreaElement>('Instructions, bloc 1 (texte)');
    zone.setSelectionRange(9, 19);
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Gras' }));
    expect(onChange).toHaveBeenLastCalledWith('faire la **sauvegarde**');
  });

  it('bloc code : Tab insère deux espaces, langage facultatif conservé', async () => {
    const onChange = vi.fn();
    render(<Hote initial={'```\nif x\n```'} onChange={onChange} />);
    const zone = screen.getByLabelText<HTMLTextAreaElement>('Instructions, bloc 1 (code)');
    zone.focus();
    zone.setSelectionRange(4, 4);
    await userEvent.keyboard('{Tab}');
    expect(onChange).toHaveBeenLastCalledWith('```\nif x  \n```');
    await userEvent.type(screen.getByLabelText('Instructions, bloc 1 : langage (facultatif)'), 'sh');
    expect(onChange).toHaveBeenLastCalledWith('```sh\nif x  \n```');
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
