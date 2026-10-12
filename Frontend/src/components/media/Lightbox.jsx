import { lazy, Suspense } from 'react'

// The viewer (~40 KB with styles) is only downloaded the first time someone opens a photo.
const LightboxImpl = lazy(() => import('./LightboxImpl.jsx'))

export function Lightbox({ open, ...props }) {
  if (!open) return null
  return (
    <Suspense fallback={null}>
      <LightboxImpl {...props} />
    </Suspense>
  )
}
