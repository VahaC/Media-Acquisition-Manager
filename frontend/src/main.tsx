import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import './styles/ui-primitives.css'
import './lib/i18n' // loads the translations before the first render
import './lib/theme-store' // System follows the OS on every page, not only while a theme switch is open
import App from './App'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
