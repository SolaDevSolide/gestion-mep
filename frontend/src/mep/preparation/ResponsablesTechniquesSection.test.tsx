import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MepContext } from '../../mepContext';
import { api } from '../../api';
import { mep, utilisateur } from '../../test/fabrique';
import { TooltipProvider } from '../../ui/Tooltip';
import { ResponsablesTechniquesSection } from './ResponsablesTechniquesSection';
import type { Mep, Utilisateur } from '../../types';

const { utilisateurs } = vi.hoisted(() => ({ utilisateurs: { liste: [] as Utilisateur[] } }));
vi.mock('../../api', () => ({ api: { modifierResponsablesTechniques: vi.fn(), ajouterParticipant: vi.fn(), utilisateurs: vi.fn(async () => utilisateurs.liste) } }));

function rendre(m: Mep) {
  const run = vi.fn(async (action?: () => Promise<unknown>) => { await action?.(); return true; });
  render(<MepContext.Provider value={{ mep: m, run } as never}><TooltipProvider><ResponsablesTechniquesSection /></TooltipProvider></MepContext.Provider>);
  return { run };
}

describe('Section Responsables techniques', () => {
  it('liste les responsables avec périmètre, systèmes, disponibilité et contact', () => {
    rendre(mep({
      responsablesTechniques: [{ participantId: 'p2', systemeIds: ['s1'], perimetre: 'API de facturation', disponibilite: 'Absent', contactJourJ: '06 00 00 00 00' }],
    }));
    const ligne = screen.getByRole('listitem');
    expect(ligne).toHaveTextContent('Bob');
    expect(ligne).toHaveTextContent('Absent');
    expect(ligne).toHaveTextContent('API de facturation');
    expect(ligne).toHaveTextContent('06 00 00 00 00');
    expect(within(ligne).getByLabelText('Systèmes développés')).toHaveTextContent('API');
  });

  it('lecture seule sans droit d’édition : pas de bouton Ajouter', () => {
    rendre(mep());
    expect(screen.queryByRole('button', { name: /Ajouter/ })).toBeNull();
    expect(screen.getByText('Aucun responsable technique')).toBeInTheDocument();
  });

  it('ajoute un responsable technique : un participant déjà responsable n’est plus proposé', async () => {
    const m = mep({
      responsablesTechniques: [{ participantId: 'p2', systemeIds: [], perimetre: '', disponibilite: 'Joignable', contactJourJ: '' }],
      droits: { ...mep().droits, peutEditerContenu: true, peutModifierPlan: true },
    });
    const { run } = rendre(m);
    await userEvent.click(screen.getByRole('button', { name: /Ajouter/ }));
    const select = screen.getByLabelText(/Participant/);
    expect(within(select).queryByRole('option', { name: /Bob/ })).toBeNull();
    expect(within(select).getByRole('option', { name: /Claire/ })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Contact le jour J'), '0102');
    await userEvent.click(screen.getByRole('radio', { name: 'Absent' }));
    const boutons = screen.getAllByRole('button', { name: 'Ajouter' });
    await userEvent.click(boutons[boutons.length - 1]);
    expect(run).toHaveBeenCalledTimes(1);
  });

  describe('suggestions d’après les expertises', () => {
    const systemeApi = { ...mep().systemes[0], id: 's1', nom: 'API', catalogueId: 'c-api' };
    const bob = utilisateur({ id: 'u2', nomAffiche: 'Bob', role: 'Développeur API', contact: '06 11', expertises: [{ systemeCatalogueId: 'c-api', detail: 'Expert de la zone facturation' }] });
    const droits = { ...mep().droits, peutEditerContenu: true, peutModifierPlan: true };

    beforeEach(() => { utilisateurs.liste = [bob, utilisateur({ id: 'u9', nomAffiche: 'Zoé', expertises: [{ systemeCatalogueId: 'autre', detail: 'ailleurs' }] })]; vi.clearAllMocks(); });

    it('propose les comptes experts d’un système de la MEP, avec le détail de l’expertise', async () => {
      rendre(mep({ systemes: [systemeApi], droits }));
      const bloc = await screen.findByRole('region', { name: /Suggestions d’après les systèmes de la MEP/ });
      expect(within(bloc).getByText('Bob')).toBeInTheDocument();
      expect(within(bloc).getByText('Expert de la zone facturation')).toBeInTheDocument();
      expect(within(bloc).getByText('API')).toBeInTheDocument();
      expect(within(bloc).queryByText('Zoé')).toBeNull();
    });

    it('Ajouter crée le participant pré-rempli puis le désigne responsable technique avec le détail pour périmètre', async () => {
      const apresAjout = mep({
        systemes: [systemeApi], droits, version: 2,
        participants: [...mep().participants.filter((p) => p.utilisateurId !== 'u2'), { id: 'p9', nom: 'Bob', role: 'Développeur API', contact: '06 11', utilisateurId: 'u2', utilisateurNom: 'Bob' }],
      });
      vi.mocked(api.ajouterParticipant).mockResolvedValue(apresAjout);
      const { run } = rendre(mep({ systemes: [systemeApi], droits, participants: mep().participants.filter((p) => p.utilisateurId !== 'u2') }));
      await userEvent.click(await screen.findByRole('button', { name: /Ajouter Bob comme responsable technique/ }));
      await waitFor(() => expect(run).toHaveBeenCalledTimes(1));
      expect(api.ajouterParticipant).toHaveBeenCalledWith('m1', { nom: 'Bob', role: 'Développeur API', contact: '06 11', utilisateurId: 'u2' });
      expect(api.modifierResponsablesTechniques).toHaveBeenCalledWith('m1', 2, [
        { participantId: 'p9', systemeIds: ['s1'], perimetre: 'Expert de la zone facturation', disponibilite: 'Joignable', contactJourJ: '06 11' },
      ]);
    });

    it('un compte déjà responsable technique n’est plus suggéré', async () => {
      rendre(mep({
        systemes: [systemeApi], droits,
        responsablesTechniques: [{ participantId: 'p2', systemeIds: ['s1'], perimetre: '', disponibilite: 'Joignable', contactJourJ: '' }],
      }));
      await screen.findByText('Bob');
      expect(screen.queryByRole('region', { name: /Suggestions/ })).toBeNull();
    });

    it('lecture seule : suggestions visibles mais sans bouton Ajouter', async () => {
      rendre(mep({ systemes: [systemeApi] }));
      await screen.findByRole('region', { name: /Suggestions/ });
      expect(screen.queryByRole('button', { name: /Ajouter/ })).toBeNull();
    });
  });
});
