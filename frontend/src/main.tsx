import '@fontsource-variable/inter';
import '@fontsource-variable/jetbrains-mono';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter, RouterProvider } from 'react-router-dom';
import App from './App';
import { AuthProvider } from './auth';
import { ThemeProvider } from './theme';
import { ToastProvider } from './ui/Toast';
import { TooltipProvider } from './ui/Tooltip';
import './index.css';

// Routeur de données : il permet de retenir une navigation (modifications non enregistrées, voir GardeModifications).
// Une seule route attrape tout ; App déclare ensuite ses pages avec <Routes>.
const routeur = createBrowserRouter([
  {
    path: '*',
    element: (
      <TooltipProvider>
        <ToastProvider>
          <AuthProvider>
            <App />
          </AuthProvider>
        </ToastProvider>
      </TooltipProvider>
    ),
  },
]);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <RouterProvider router={routeur} />
    </ThemeProvider>
  </StrictMode>,
);
