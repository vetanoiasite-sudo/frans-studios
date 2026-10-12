import { useEffect } from 'react'

/**
 * Keeps document.title in sync during client-side navigation. The initial title, description,
 * Open Graph and JSON-LD tags are rendered by the server (SeoMiddleware) for crawlers.
 */
export function usePageTitle(title) {
  useEffect(() => {
    if (title) document.title = title.includes('Frans Studios') ? title : `${title} — Frans Studios`
  }, [title])
}
