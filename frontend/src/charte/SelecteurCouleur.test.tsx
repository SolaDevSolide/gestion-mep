import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { PanneauCouleur } from './SelecteurCouleur';

function Panneau({ onChange, initiale = '#4f46e5' }: { onChange: (h: string) => void; initiale?: string }) {
  const [valeur, setValeur] = useState(initiale);
  return (
    <PanneauCouleur
      titre="Primaire · thème clair"
      valeur={valeur}
      initiale={initiale}
      charte={[{ libelle: 'Secondaire', couleur: '#1b2a41' }]}
      onChange={(h) => {
        setValeur(h);
        onChange(h);
      }}
    />
  );
}

describe('Sélecteur de couleur', () => {
  beforeEach(() => window.localStorage.clear());

  it('prend un code hexadécimal, court ou sans « # », et signale un code incomplet', async () => {
    const onChange = vi.fn();
    render(<Panneau onChange={onChange} />);
    const champ = screen.getByLabelText('Code hexadécimal');
    await userEvent.clear(champ);
    await userEvent.type(champ, '0F766E');
    expect(onChange).toHaveBeenLastCalledWith('#0f766e');
    expect(screen.getByText('Texte blanc')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).toBeNull();

    await userEvent.clear(champ);
    await userEvent.type(champ, '12');
    expect(screen.getByRole('alert')).toHaveTextContent('6 chiffres hexadécimaux attendus');
    await userEvent.type(champ, '3');
    expect(onChange).toHaveBeenLastCalledWith('#112233');
  });

  it('se règle au clavier et propose les couleurs de la charte, retenues comme récentes', async () => {
    const onChange = vi.fn();
    render(<Panneau onChange={onChange} />);
    fireEvent.keyDown(screen.getByRole('slider', { name: 'Teinte' }), { key: 'ArrowRight', shiftKey: true });
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0][0]).not.toBe('#4f46e5');

    await userEvent.click(screen.getByRole('button', { name: 'Secondaire #1B2A41' }));
    expect(onChange).toHaveBeenLastCalledWith('#1b2a41');
    expect(JSON.parse(window.localStorage.getItem('mep.charte.recentes') ?? '[]')).toEqual(['#1b2a41']);
  });

  it('se saisit aussi en RVB', async () => {
    const onChange = vi.fn();
    render(<Panneau onChange={onChange} />);
    await userEvent.click(screen.getByRole('button', { name: 'RVB' }));
    const rouge = screen.getByLabelText('Rouge');
    expect(rouge).toHaveValue(79);
    fireEvent.change(rouge, { target: { value: '15' } });
    expect(onChange).toHaveBeenLastCalledWith('#0f46e5');
  });
});
