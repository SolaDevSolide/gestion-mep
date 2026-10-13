import { api } from './api';
import { useAsync } from './hooks';

/** Type d'installation du serveur : image Docker, ou installation sans Docker sur Linux ou Windows. */
export type ModeInstallation = 'docker' | 'linux' | 'windows';

/** Commande d'exploitation à taper sur le serveur, selon l'installation. */
export function commande(mode: ModeInstallation, args: string) {
  if (mode === 'linux') return `sudo gestion-mep ${args}`;
  if (mode === 'windows') return `gestion-mep ${args}`;
  return `docker compose exec app gestion-mep ${args}`;
}

/** Commande qui lit un secret dans un fichier (sans qu'il apparaisse dans l'historique ni dans la liste des processus). */
export function commandeAvecSecret(mode: ModeInstallation, args: string) {
  if (mode === 'linux') return `sudo gestion-mep ${args} < secret.txt`;
  if (mode === 'windows') return `Get-Content secret.txt | gestion-mep ${args}`;
  return `docker compose exec -T app gestion-mep ${args} < secret.txt`;
}

/** Où changer un réglage lu au démarrage, et comment le prendre en compte. */
export function fichierDemarrage(mode: ModeInstallation) {
  if (mode === 'linux')
    return 'Lus dans /etc/gestion-mep/gestion-mep.conf au lancement : les modifier dans ce fichier, puis « sudo systemctl restart gestion-mep ».';
  if (mode === 'windows')
    return 'Lus dans C:\\ProgramData\\GestionMep\\gestion-mep.conf au lancement : les modifier dans ce fichier, puis « Restart-Service GestionMep ».';
  return 'Lus dans le fichier .env au lancement : les modifier dans ce fichier, puis « docker compose up -d ».';
}

/** Installation du serveur (Docker tant qu'elle n'est pas connue). */
export function useInstallation(): ModeInstallation {
  // Promise.resolve : une erreur de chargement (ou un client sans cette route) garde la valeur par défaut.
  const { donnees } = useAsync(() => Promise.resolve().then(() => api.application()), []);
  const mode = donnees?.installation;
  return mode === 'linux' || mode === 'windows' ? mode : 'docker';
}
