import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MultiSelect } from './Combobox';

const options = [
  { valeur: 'a', libelle: 'Alice' },
  { valeur: 'b', libelle: 'Bob' },
];

describe('MultiSelect', () => {
  it('les boutons « Retirer » ne sont pas imbriqués dans le déclencheur et retirent la valeur', async () => {
    const onChange = vi.fn();
    render(<MultiSelect id="m" options={options} valeurs={['a', 'b']} onChange={onChange} placeholder="Personne" />);
    const retirer = screen.getByRole('button', { name: 'Retirer Alice' });
    expect(retirer.parentElement?.closest('button')).toBeNull();
    await userEvent.click(retirer);
    expect(onChange).toHaveBeenCalledWith(['b']);
  });

  it('le déclencheur ouvre la liste', async () => {
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    );
    Element.prototype.scrollIntoView ??= () => undefined;
    render(
      <MultiSelect
        options={options}
        valeurs={['a']}
        onChange={vi.fn()}
        placeholder="Personne"
        placeholderRecherche="Chercher"
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: '1 sélectionné' }));
    expect(screen.getByPlaceholderText('Chercher')).toBeInTheDocument();
    vi.unstubAllGlobals();
  });
});
