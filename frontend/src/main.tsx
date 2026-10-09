import '@fontsource-variable/inter';
import '@fontsource-variable/jetbrains-mono';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import { AuthProvider } from './auth';
import { ThemeProvider } from './theme';
import { ToastProvider } from './ui/Toast';
import { TooltipProvider } from './ui/Tooltip';
import './index.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <BrowserRouter>
        <TooltipProvider>
          <ToastProvider>
            <AuthProvider>
              <App />
            </AuthProvider>
          </ToastProvider>
        </TooltipProvider>
      </BrowserRouter>
    </ThemeProvider>
  </StrictMode>,
);
