// dompurify 3.1.6 ne fournit pas ses types : déclaration minimale de ce que l'application utilise.
declare module 'dompurify' {
  interface DOMPurifyI {
    sanitize(source: string, config?: Record<string, unknown>): string;
  }
  const DOMPurify: DOMPurifyI;
  export default DOMPurify;
}
