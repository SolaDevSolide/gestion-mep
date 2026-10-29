import {
  CircleArrowUp,
  KeyRound,
  NotebookText,
  Palette,
  SlidersHorizontal,
  Users,
  UsersRound,
  Webhook,
} from 'lucide-react';
import { useEffect, useRef } from 'react';
import { Navigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../ui/Tabs';
import ApiExternesTab from './ApiExternesTab';
import CharteTab from './CharteTab';
import ComptesTab from './ComptesTab';
import EquipesTab from './EquipesTab';
import JournalTab from './JournalTab';
import ParametresTab from './ParametresTab';
import SsoTab from './SsoTab';
import VersionTab from './VersionTab';

const ONGLETS = new Set(['comptes', 'equipes', 'parametres', 'charte', 'sso', 'api', 'journal', 'version']);

/** Console d'administration : comptes, équipes, paramètres globaux et journal. Réservée aux administrateurs. */
export default function AdminPage() {
  const { utilisateur } = useAuth();
  const [params, setParams] = useSearchParams();
  const demande = params.get('onglet');
  const onglet = demande && ONGLETS.has(demande) ? demande : 'comptes';
  const rangee = useRef<HTMLDivElement>(null);
  // Écran étroit : les onglets défilent ; l'onglet ouvert (lien direct, retour de l'éditeur) est ramené en vue.
  useEffect(() => {
    rangee.current
      ?.querySelector<HTMLElement>('[data-state="active"]')
      ?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
  }, [onglet]);
  if (!utilisateur?.estAdmin) return <Navigate to="/" replace />;
  return (
    <main className={`mx-auto w-full ${onglet === 'charte' ? 'max-w-6xl' : 'max-w-5xl'} px-6 py-8`}>
      <h1 className="text-2xl font-semibold tracking-tight">Console d’administration</h1>
      <p className="mt-1 text-sm text-fg-subtle">
        Gérez les comptes, les équipes, les paramètres globaux, les connexions aux API externes et la version de
        l’application, et consultez le journal des actions d’administration.
      </p>
      <Tabs value={onglet} onValueChange={(o) => setParams({ onglet: o }, { replace: true })} className="mt-6">
        <TabsList ref={rangee} className="border-b border-line" aria-label="Sections de la console">
          <TabsTrigger value="comptes">
            <Users className="size-4" aria-hidden /> Comptes
          </TabsTrigger>
          <TabsTrigger value="equipes">
            <UsersRound className="size-4" aria-hidden /> Équipes
          </TabsTrigger>
          <TabsTrigger value="parametres">
            <SlidersHorizontal className="size-4" aria-hidden /> Paramètres
          </TabsTrigger>
          <TabsTrigger value="charte">
            <Palette className="size-4" aria-hidden /> Charte visuelle
          </TabsTrigger>
          <TabsTrigger value="sso">
            <KeyRound className="size-4" aria-hidden /> Connexion unique
          </TabsTrigger>
          <TabsTrigger value="api">
            <Webhook className="size-4" aria-hidden /> API externes
          </TabsTrigger>
          <TabsTrigger value="journal">
            <NotebookText className="size-4" aria-hidden /> Journal
          </TabsTrigger>
          <TabsTrigger value="version">
            <CircleArrowUp className="size-4" aria-hidden /> Version
          </TabsTrigger>
        </TabsList>
        <TabsContent value="comptes" className="mt-6 outline-none">
          <ComptesTab />
        </TabsContent>
        <TabsContent value="equipes" className="mt-6 outline-none">
          <EquipesTab />
        </TabsContent>
        <TabsContent value="parametres" className="mt-6 outline-none">
          <ParametresTab />
        </TabsContent>
        <TabsContent value="charte" className="mt-6 outline-none">
          <CharteTab />
        </TabsContent>
        <TabsContent value="sso" className="mt-6 outline-none">
          <SsoTab />
        </TabsContent>
        <TabsContent value="api" className="mt-6 outline-none">
          <ApiExternesTab />
        </TabsContent>
        <TabsContent value="journal" className="mt-6 outline-none">
          <JournalTab />
        </TabsContent>
        <TabsContent value="version" className="mt-6 outline-none">
          <VersionTab />
        </TabsContent>
      </Tabs>
    </main>
  );
}
