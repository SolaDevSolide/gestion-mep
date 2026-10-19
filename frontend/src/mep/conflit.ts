const PAR_DEFAUT = 'Quelqu’un a modifié cette MEP entre-temps : elle est à jour, votre saisie est conservée.';
const LECTURE = ' depuis votre dernière lecture';

/**
 * Message affiché quand le serveur refuse une écriture pour modification simultanée (409) : ce qui a changé, tel que le
 * serveur le nomme (« Plan de retour arrière » a été modifié par quelqu'un d'autre…, E0004 a été modifiée…), et la saisie
 * conservée, puisque la MEP vient d'être relue sans toucher aux brouillons.
 */
export function messageConflit(message: string | undefined): string {
  const texte = (message ?? '').trim();
  if (!texte) return PAR_DEFAUT;
  const i = texte.indexOf(LECTURE);
  if (i > 0) return `${texte.slice(0, i)} : la MEP est à jour, votre saisie est conservée.`;
  return `${texte} Votre saisie est conservée.`;
}
