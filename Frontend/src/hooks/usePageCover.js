import { useApi } from './useApi.js'

/**
 * Cover photo uploaded for a public page in Admin → Page covers (e.g. 'packages', 'contact', 'about').
 * Returns the image (for <ResponsiveImage>), null when none is uploaded (pages then use their built-in
 * default), or undefined while loading — so a page doesn't flash its default before the real cover.
 */
export function usePageCover(page) {
  const { data, error } = useApi('/api/public/page-covers')
  if (error) return null
  return data ? (data[page] ?? null) : undefined
}
