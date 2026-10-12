// Global styles first, so area stylesheets (public/client/admin) can override them.
import '@fontsource/cormorant-garamond/300.css'
import '@fontsource/cormorant-garamond/400.css'
import '@fontsource/cormorant-garamond/400-italic.css'
import '@fontsource/cormorant-garamond/500.css'
import '@fontsource/dm-sans/400.css'
import '@fontsource/dm-sans/500.css'
import './styles/tokens.css'
import './styles/base.css'

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router/dom'
import { router } from './routes/router.jsx'
import { direction, currentLocale } from './i18n/index.js'

document.documentElement.lang = currentLocale
document.documentElement.dir = direction

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
)
