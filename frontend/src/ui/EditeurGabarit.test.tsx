import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import type { CatalogueGabarit } from '../textes/gabarits';
import type { VariableTexte } from '../types';
import { EditeurGabarit, jetonEnCours, variablesProposees } from './EditeurGabarit';

const variables: VariableTexte[] = [
  { cle: 'pilote', libelle: 'Pilote', groupe: 'MEP', description: 'Nom du pilote', liste: false },
  { cle: 'pilote_contact', libelle: 'Contact du pilote', groupe: 'MEP', description: 'Téléphone', liste: false },
  { cle: 'etape_systemes', libelle: 'Systèmes de l’étape', groupe: 'Événement', description: '', liste: true },
];
const catalogue: CatalogueGabarit = new Map(variables.map((v) => [v.cle, v.liste]));

function Editeur({ depart = '', onChange }: { depart?: string; onChange?: (v: string) => void }) {
  const [v, setV] = useState(depart);
  return (
    <EditeurGabarit
      aria-label="Message"
      value={v}
      onChange={(x) => {
        setV(x);
        onChange?.(x);
      }}
      format="Texte"
      catalogue={catalogue}
      variables={variables}
    />
  );
}

describe('EditeurGabarit', () => {
  it('repère la variable en cours de frappe', () => {
    expect(jetonEnCours('Bonjour {{pil', 13)).toEqual({ debut: 8, recherche: 'pil' });
    expect(jetonEnCours('Bonjour {{pilote}} ', 19)).toBeNull();
    expect(variablesProposees(variables, 'contact').map((v) => v.cle)).toEqual(['pilote_contact']);
    expect(variablesProposees(variables, 'etape sys').map((v) => v.cle)).toEqual(['etape_systemes']);
  });

  it('« {{ » ouvre l’autocomplétion, Entrée insère la variable choisie', async () => {
    const onChange = vi.fn();
    render(<Editeur onChange={onChange} />);
    const zone = screen.getByRole('combobox', { name: 'Message' });
    await userEvent.type(zone, 'Bonjour {{{{pil');
    expect(zone).toHaveAttribute('aria-expanded', 'true');
    const options = screen.getAllByRole('option');
    expect(options.map((o) => o.textContent)).toEqual([
      expect.stringContaining('{{pilote}}'),
      expect.stringContaining('{{pilote_contact}}'),
    ]);
    await userEvent.keyboard('{ArrowDown}');
    expect(screen.getAllByRole('option')[1]).toHaveAttribute('aria-selected', 'true');
    await userEvent.keyboard('{Enter}');
    expect(onChange).toHaveBeenLastCalledWith('Bonjour {{pilote_contact}}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('Échap ferme la liste sans rien insérer', async () => {
    render(<Editeur depart="x " />);
    const zone = screen.getByRole('combobox', { name: 'Message' });
    await userEvent.click(zone);
    await userEvent.type(zone, '{{{{');
    expect(screen.getByRole('listbox')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(zone).toHaveValue('x {{');
  });

  it('souligne une variable inconnue dans le calque', () => {
    const { container } = render(<Editeur depart="{{pilote}} et {{pilot}}" />);
    const calque = container.querySelector('pre');
    const morceaux = [...(calque?.querySelectorAll('span') ?? [])];
    expect(morceaux.find((s) => s.textContent === '{{pilot}}')?.className).toContain('decoration-wavy');
    expect(morceaux.find((s) => s.textContent === '{{pilote}}')?.className).not.toContain('decoration-wavy');
  });
});
