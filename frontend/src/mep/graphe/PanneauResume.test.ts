import { dureeCumulee } from './PanneauResume';

describe('durée cumulée (durée facultative)', () => {
  it('ignore les étapes sans durée et les signale', () => {
    expect(
      dureeCumulee([
        { dureeEstimeeMinutes: 20 },
        { dureeEstimeeMinutes: null },
        { dureeEstimeeMinutes: 125 },
        { dureeEstimeeMinutes: null },
      ]),
    ).toBe('145 min (2 étapes sans durée)');
    expect(dureeCumulee([{ dureeEstimeeMinutes: 10 }])).toBe('10 min');
    expect(dureeCumulee([{ dureeEstimeeMinutes: null }])).toBe('durée non estimée');
  });
});
