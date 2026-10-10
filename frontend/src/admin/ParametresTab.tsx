import { ArrowRight, OctagonAlert, Save, ShieldCheck } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useAsync } from '../hooks';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { Field, Input } from '../ui/Input';
import { useToast } from '../ui/Toast';

export default function ParametresTab() {
  const toast = useToast();
  const { donnees, erreur, recharger } = useAsync(() => api.parametres(), []);
  const [valeur, setValeur] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);
  const courant = valeur ?? String(donnees?.approbationsMinimum ?? 1);
  const nombre = Number(courant);
  const valide = courant !== '' && Number.isInteger(nombre) && nombre >= 0;
  const modifie = donnees !== null && valide && nombre !== donnees.approbationsMinimum;

  async function enregistrer() {
    setEnvoi(true);
    try {
      await api.modifierParametres({ approbationsMinimum: nombre });
      toast.succes('Paramètre enregistré.');
      setValeur(null);
      recharger();
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <div className="space-y-6">
      {erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert">
          {erreur}
        </Alerte>
      )}
      <Card>
        <CardHeader
          titre="Approbations"
          description="Nombre d’approbations nécessaires avant de pouvoir donner le Go d’une MEP."
        />
        <div className="px-5 py-5">
          <Field label="Nombre minimum d’approbations" hint="0 = aucune approbation requise." className="max-w-xs">
            {(id) => (
              <Input
                id={id}
                type="number"
                min={0}
                step={1}
                value={courant}
                disabled={donnees === null}
                onChange={(e) => setValeur(e.target.value)}
              />
            )}
          </Field>
        </div>
        <div className="flex items-center gap-3 border-t border-line bg-surface-2/50 px-5 py-3">
          <Button
            variante="primaire"
            icone={<Save className="size-4" aria-hidden />}
            disabled={!modifie}
            chargement={envoi}
            onClick={() => void enregistrer()}
          >
            Enregistrer
          </Button>
        </div>
      </Card>
      <Card>
        <CardHeader titre="Critères Go / no-go" description="Vérifications cochées par le pilote avant le Go." />
        <div className="px-5 py-5">
          <Link
            to="/parametres/go-no-go"
            className="inline-flex items-center gap-2 text-sm font-medium text-accent-text hover:underline"
          >
            <ShieldCheck className="size-4" aria-hidden /> Gérer les critères Go / no-go{' '}
            <ArrowRight className="size-4" aria-hidden />
          </Link>
        </div>
      </Card>
    </div>
  );
}
