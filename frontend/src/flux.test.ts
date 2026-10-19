import { ouvrirFlux } from './flux';

class FauxFlux {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSED = 2;
  static instances: FauxFlux[] = [];
  readyState = FauxFlux.CONNECTING;
  ferme = false;
  private ecouteurs = new Map<string, ((ev: Event) => void)[]>();
  constructor(public url: string) {
    FauxFlux.instances.push(this);
  }
  addEventListener(type: string, f: (ev: Event) => void) {
    this.ecouteurs.set(type, [...(this.ecouteurs.get(type) ?? []), f]);
  }
  close() {
    this.ferme = true;
    this.readyState = FauxFlux.CLOSED;
  }
  emettre(type: string, ev: Event = new Event(type)) {
    for (const f of this.ecouteurs.get(type) ?? []) f(ev);
  }
  ouvrir() {
    this.readyState = FauxFlux.OPEN;
    this.emettre('open');
  }
  echouer(definitif: boolean) {
    this.readyState = definitif ? FauxFlux.CLOSED : FauxFlux.CONNECTING;
    this.emettre('error');
  }
}

describe('ouvrirFlux', () => {
  beforeEach(() => {
    FauxFlux.instances = [];
    vi.useFakeTimers();
    vi.stubGlobal('EventSource', FauxFlux);
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('transmet les versions reçues', () => {
    const surMaj = vi.fn();
    ouvrirFlux('/api/meps/m1/flux', surMaj, vi.fn());
    FauxFlux.instances[0].emettre('maj', new MessageEvent('maj', { data: '{"version":7}' }));
    FauxFlux.instances[0].emettre('maj', new MessageEvent('maj', { data: 'illisible' }));
    expect(surMaj).toHaveBeenCalledTimes(1);
    expect(surMaj).toHaveBeenCalledWith(7, { version: 7, meps: undefined, liste: undefined });
  });

  it('transmet les cartes du suivi à relire, ou l’ordre de tout relire', () => {
    const surMaj = vi.fn();
    ouvrirFlux('/api/suivi/flux', surMaj, vi.fn());
    const flux = FauxFlux.instances[0];
    flux.emettre('maj', new MessageEvent('maj', { data: '{"version":3,"meps":["m1","m2"],"liste":false}' }));
    flux.emettre('maj', new MessageEvent('maj', { data: '{"version":4,"meps":[],"liste":true}' }));
    flux.emettre('maj', new MessageEvent('maj', { data: '{"meps":["m1"]}' }));
    expect(surMaj.mock.calls).toEqual([
      [3, { version: 3, meps: ['m1', 'm2'], liste: false }],
      [4, { version: 4, meps: [], liste: true }],
    ]);
  });

  it('transmet la présence et prévient à chaque ouverture, la première comprise', () => {
    const surPresence = vi.fn();
    const surOuverture = vi.fn();
    ouvrirFlux('/api/meps/m1/flux?session=s1', vi.fn(), vi.fn(), { surPresence, surOuverture });
    const flux = FauxFlux.instances[0];
    flux.ouvrir();
    expect(surOuverture).toHaveBeenCalledTimes(1);
    const presences = [{ utilisateurId: 'u1', nom: 'Bob Martin', elements: ['etape:e1'] }];
    flux.emettre('presence', new MessageEvent('presence', { data: JSON.stringify(presences) }));
    flux.emettre('presence', new MessageEvent('presence', { data: '{"pas":"une liste"}' }));
    flux.emettre('presence', new MessageEvent('presence', { data: 'illisible' }));
    expect(surPresence).toHaveBeenCalledTimes(1);
    expect(surPresence).toHaveBeenCalledWith(presences);
    flux.echouer(false);
    flux.ouvrir();
    expect(surOuverture).toHaveBeenCalledTimes(2);
  });

  it('recrée le flux fermé après une erreur HTTP, avec un délai croissant, puis resynchronise', () => {
    const surReconnexion = vi.fn();
    ouvrirFlux('/api/meps/m1/flux', vi.fn(), surReconnexion);
    FauxFlux.instances[0].ouvrir();
    expect(surReconnexion).not.toHaveBeenCalled();

    FauxFlux.instances[0].echouer(true);
    vi.advanceTimersByTime(999);
    expect(FauxFlux.instances).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(FauxFlux.instances).toHaveLength(2);

    FauxFlux.instances[1].echouer(true);
    vi.advanceTimersByTime(1_999);
    expect(FauxFlux.instances).toHaveLength(2);
    vi.advanceTimersByTime(1);
    expect(FauxFlux.instances).toHaveLength(3);

    FauxFlux.instances[2].ouvrir();
    expect(surReconnexion).toHaveBeenCalledTimes(1);

    // Après une réouverture réussie, le délai repart de 1 s.
    FauxFlux.instances[2].echouer(true);
    vi.advanceTimersByTime(1_000);
    expect(FauxFlux.instances).toHaveLength(4);
  });

  it('laisse le navigateur gérer une coupure réseau (flux en reconnexion), mais resynchronise à la réouverture', () => {
    const surReconnexion = vi.fn();
    ouvrirFlux('/api/meps/m1/flux', vi.fn(), surReconnexion);
    FauxFlux.instances[0].ouvrir();
    FauxFlux.instances[0].echouer(false);
    vi.advanceTimersByTime(60_000);
    expect(FauxFlux.instances).toHaveLength(1);
    FauxFlux.instances[0].ouvrir();
    expect(surReconnexion).toHaveBeenCalledTimes(1);
  });

  it('plafonne le délai à 30 s et s’arrête à la fermeture', () => {
    const fermer = ouvrirFlux('/api/meps/m1/flux', vi.fn(), vi.fn());
    for (let i = 0; i < 6; i++) {
      FauxFlux.instances.at(-1)!.echouer(true);
      vi.advanceTimersByTime(30_000);
    }
    expect(FauxFlux.instances).toHaveLength(7);
    FauxFlux.instances.at(-1)!.echouer(true);
    fermer();
    vi.advanceTimersByTime(60_000);
    expect(FauxFlux.instances).toHaveLength(7);
    expect(FauxFlux.instances.at(-1)!.ferme).toBe(true);
  });
});
