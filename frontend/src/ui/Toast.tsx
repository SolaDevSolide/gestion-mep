import { CheckCircle2, OctagonAlert, X } from 'lucide-react';
import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react';
import { cn } from './cn';

interface Message {
  id: number;
  type: 'succes' | 'erreur';
  texte: string;
}
interface ToastApi {
  succes: (t: string) => void;
  erreur: (t: string) => void;
}

const ToastContext = createContext<ToastApi>({ succes: () => undefined, erreur: () => undefined });
export const useToast = () => useContext(ToastContext);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [messages, setMessages] = useState<Message[]>([]);
  const compteur = useRef(0);

  const retirer = useCallback((id: number) => setMessages((m) => m.filter((x) => x.id !== id)), []);
  const ajouter = useCallback(
    (type: Message['type'], texte: string) => {
      const id = ++compteur.current;
      setMessages((m) => [...m.slice(-3), { id, type, texte }]);
      window.setTimeout(() => retirer(id), type === 'succes' ? 4000 : 9000);
    },
    [retirer],
  );

  const api = useMemo<ToastApi>(
    () => ({ succes: (t) => ajouter('succes', t), erreur: (t) => ajouter('erreur', t) }),
    [ajouter],
  );

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div className="pointer-events-none fixed right-4 bottom-4 z-[80] flex w-[min(24rem,calc(100vw-2rem))] flex-col gap-2">
        {messages.map((m) => (
          <div
            key={m.id}
            role={m.type === 'erreur' ? 'alert' : 'status'}
            className={cn(
              'pointer-events-auto flex animate-toast-in items-start gap-3 rounded-xl border bg-surface px-4 py-3 shadow-lg',
              m.type === 'erreur' ? 'border-danger-bd' : 'border-ok-bd',
            )}
          >
            {m.type === 'erreur' ? (
              <OctagonAlert className="mt-0.5 size-5 shrink-0 text-danger-solid" aria-hidden />
            ) : (
              <CheckCircle2 className="mt-0.5 size-5 shrink-0 text-ok-solid" aria-hidden />
            )}
            <p className="min-w-0 flex-1 text-sm whitespace-pre-line text-fg">{m.texte}</p>
            <button
              type="button"
              aria-label="Fermer"
              onClick={() => retirer(m.id)}
              className="-mr-1 rounded p-0.5 text-fg-subtle hover:text-fg"
            >
              <X className="size-4" aria-hidden />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}
