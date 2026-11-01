import { defaultStories } from '../content/portfolio.js'
import { useApi } from './useApi.js'

const card = (s) => ({ ...s, cover: s.images[0], imageCount: s.images.length, images: undefined })

/** Built-in stories in the GalleryCard shape of /api/public/galleries. */
export const defaultStoryCards = defaultStories.map(card)

/** Built-in photos in the shape of /api/public/portfolio (every photo of every story, in story order). */
export const defaultWall = defaultStories.flatMap((s) => s.images.map((image) => ({ image, gallerySlug: s.slug, galleryTitle: s.title })))

/** One built-in story in the GalleryDetail shape of /api/public/galleries/{slug}, or null. */
export function defaultStory(slug) {
  const i = defaultStories.findIndex((s) => s.slug === slug)
  if (i < 0) return null
  const s = defaultStories[i]
  const nav = (x) => (x ? { slug: x.slug, title: x.title } : null)
  return { ...s, cover: s.images[0], previous: nav(defaultStories[i - 1]), next: nav(defaultStories[i + 1]) }
}

/**
 * Published stories from Admin → Portfolio, or the built-in ones when none are published (or the API
 * fails), so the home page and the Portfolio page never show an empty or broken album list.
 */
export function useStories() {
  const { data, error, loading } = useApi('/api/public/galleries')
  if (loading) return { data: null, loading }
  return { data: !error && data?.length ? data : defaultStoryCards, loading }
}
