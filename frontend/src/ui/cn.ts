import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/** clsx + fusion des classes Tailwind en conflit (la dernière l'emporte). */
export const cn = (...v: ClassValue[]) => twMerge(clsx(v));
