import { CircleArrowUp, NotebookText, SlidersHorizontal, Users } from 'lucide-react';
import { Navigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../ui/Tabs';
import ComptesTab from './ComptesTab';
import JournalTab from './JournalTab';
import ParametresTab from './ParametresTab';
import VersionTab from './VersionTab';

const ONGLETS = new Set(['comptes', 'parametres', 'journal', 'version']);

/** Console d'administration : comptes, paramètres globaux et journal. Réservée aux administrateurs. */
export default function AdminPage() {
  const { utilisateur } = useAuth();
  const [params, setParams] = useSearchParams();
  const demande = params.get('onglet');
  const onglet = demande && ONGLETS.has(demande) ? demande : 'comptes';
  if (!utilisateur?.estAdmin) return <Navigate to="/" replace />;
  return (
    <main className="mx-auto w-full max-w-5xl px-6 py-8">
      <h1 className="text-2xl font-semibold tracking-tight">Console d’administration</h1>
      <p className="mt-1 text-sm text-fg-subtle">
        Gérez les comptes, les paramètres globaux et la version de l’application, et consultez le journal des actions
        d’administration.
      </p>
      <Tabs value={onglet} onValueChange={(o) => setParams({ onglet: o }, { replace: true })} className="mt-6">
        <TabsList className="border-b border-line" aria-label="Sections de la console">
          <TabsTrigger value="comptes">
            <Users className="size-4" aria-hidden /> Comptes
          </TabsTrigger>
          <TabsTrigger value="parametres">
            <SlidersHorizontal className="size-4" aria-hidden /> Paramètres
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
        <TabsContent value="parametres" className="mt-6 outline-none">
          <ParametresTab />
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
