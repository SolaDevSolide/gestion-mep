/** Télécharge un fichier servi par l'API (le navigateur reste sur la page). */
export function telecharger(url: string) {
  const a = document.createElement('a');
  a.href = url;
  a.download = '';
  document.body.appendChild(a);
  a.click();
  a.remove();
}
