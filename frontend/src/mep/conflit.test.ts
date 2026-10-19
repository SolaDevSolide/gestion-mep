import { messageConflit } from './conflit';

describe('messageConflit', () => {
  it('nomme ce que quelqu’un d’autre a modifié et rassure sur la saisie', () => {
    expect(
      messageConflit(
        '« Plan de retour arrière » a été modifié par quelqu’un d’autre depuis votre dernière lecture : rechargez la MEP, vérifiez ce qui a changé, puis recommencez.',
      ),
    ).toBe(
      '« Plan de retour arrière » a été modifié par quelqu’un d’autre : la MEP est à jour, votre saisie est conservée.',
    );
    expect(
      messageConflit(
        'E0004 a été modifiée par quelqu’un d’autre depuis votre dernière lecture. Rechargez-la puis recommencez.',
      ),
    ).toBe('E0004 a été modifiée par quelqu’un d’autre : la MEP est à jour, votre saisie est conservée.');
  });

  it('garde un autre message tel quel, et a un message par défaut', () => {
    expect(
      messageConflit(
        'La MEP est en cours de modification par quelqu’un d’autre depuis trop longtemps : réessayez dans un instant.',
      ),
    ).toBe(
      'La MEP est en cours de modification par quelqu’un d’autre depuis trop longtemps : réessayez dans un instant. Votre saisie est conservée.',
    );
    expect(messageConflit('')).toBe(
      'Quelqu’un a modifié cette MEP entre-temps : elle est à jour, votre saisie est conservée.',
    );
  });
});
