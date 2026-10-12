import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { largestSrc, ResponsiveImage } from '../../components/media/ResponsiveImage.jsx'
import { Lightbox } from '../../components/media/Lightbox.jsx'
import { PortfolioStories } from '../../components/public/PortfolioStories.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { t } from '../../i18n/index.js'

const PAGE_SIZE = 60

/** 3 columns on desktop, 2 on tablets, 1 on phones (kept in sync with the window size). */
function useColumnCount() {
  const get = () => (window.matchMedia('(min-width: 900px)').matches ? 3 : window.matchMedia('(min-width: 560px)').matches ? 2 : 1)
  const [count, setCount] = useState(get)
  useEffect(() => {
    const onResize = () => setCount(get())
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])
  return count
}

/**
 * One photo of the wall. It fades in once it has scrolled into view and its image has loaded,
 * so photos never appear half-drawn. `delay` staggers the photos of a row from left to right.
 */
function WallItem({ delay, children }) {
  const ref = useRef(null)
  const [shown, setShown] = useState(false)

  useEffect(() => {
    const el = ref.current
    const img = el?.querySelector('img')
    if (!el || !img) return
    let inView = false
    let loaded = img.complete && img.naturalWidth > 0
    const reveal = () => inView && loaded && setShown(true)
    const onLoad = () => { loaded = true; reveal() }
    img.addEventListener('load', onLoad)
    img.addEventListener('error', onLoad)
    const io = new IntersectionObserver((entries) => {
      if (entries[0].isIntersecting) { inView = true; reveal(); io.disconnect() }
    }, { rootMargin: '0px 0px -8% 0px' })
    io.observe(el)
    return () => {
      io.disconnect()
      img.removeEventListener('load', onLoad)
      img.removeEventListener('error', onLoad)
    }
  }, [])

  return (
    <li ref={ref} className={`portfolio-wall__item${shown ? ' is-shown' : ''}`} style={{ '--fade-delay': `${delay}ms` }}>
      {children}
    </li>
  )
}

/**
 * Sticky "Wedding stories | All photos" bar under the site header, so visitors see from the start
 * that the page has two parts. Tabs scroll to their section; the active one follows the scroll.
 */
function PortfolioTabs({ storyCount, photoCount, galleryRef }) {
  const [active, setActive] = useState('stories')

  useEffect(() => {
    const onScroll = () => {
      const top = galleryRef.current?.getBoundingClientRect().top
      setActive(top !== undefined && top < window.innerHeight * 0.45 ? 'gallery' : 'stories')
    }
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [galleryRef])

  const go = (id) => () => {
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    if (id === 'stories') window.scrollTo({ top: 0, behavior: reduce ? 'auto' : 'smooth' })
    else document.getElementById(id)?.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth' })
  }

  return (
    <nav className="portfolio-tabs" aria-label="Portfolio sections">
      <div className="portfolio-tabs__inner">
        <button type="button" className="portfolio-tabs__tab" aria-current={active === 'stories' ? 'true' : undefined} onClick={go('stories')}>
          Wedding stories <span className="portfolio-tabs__count">{storyCount}</span>
        </button>
        <button type="button" className="portfolio-tabs__tab" aria-current={active === 'gallery' ? 'true' : undefined} onClick={go('gallery')}>
          All photos {photoCount > 0 && <span className="portfolio-tabs__count">{photoCount}</span>}
        </button>
      </div>
    </nav>
  )
}

/**
 * Portfolio wall (like fransstudios.com/portofolio): full-width, 3 columns of photos with 16px gaps,
 * scrolled continuously. Shows every photo of every published album (Admin → Portfolio), in album
 * order, loading more as the visitor nears the bottom. Clicking a photo opens it full screen.
 */
export default function PortfolioPage() {
  usePageTitle('Portfolio')
  const { data: stories } = useApi('/api/public/galleries')
  const [state, setState] = useState({ items: [], total: null, page: 0, loading: false, error: null })
  const [viewer, setViewer] = useState(-1)
  const busy = useRef(false)
  const sentinel = useRef(null)
  const galleryHead = useRef(null)

  const loadMore = useCallback(async () => {
    if (busy.current) return
    busy.current = true
    setState((s) => ({ ...s, loading: true, error: null }))
    try {
      const page = state.page + 1
      const data = await api('/api/public/portfolio', { query: { page, pageSize: PAGE_SIZE } })
      setState((s) => ({ items: [...s.items, ...data.items], total: data.total, page, loading: false, error: null }))
    } catch (error) {
      setState((s) => ({ ...s, loading: false, error }))
    } finally {
      busy.current = false
    }
  }, [state.page])

  const hasMore = state.total === null || state.items.length < state.total

  // First page on mount, then more whenever the sentinel scrolls into view.
  useEffect(() => {
    if (state.page === 0 && !state.loading && !state.error) loadMore()
  }, [state.page, state.loading, state.error, loadMore])

  useEffect(() => {
    const el = sentinel.current
    if (!el || !hasMore || state.page === 0 || state.error) return
    const io = new IntersectionObserver((entries) => entries[0].isIntersecting && loadMore(), { rootMargin: '1500px 0px' })
    io.observe(el)
    return () => io.disconnect()
  }, [hasMore, state.page, state.error, loadMore])

  const slides = state.items.map((p) => ({ src: largestSrc(p.image), width: p.image.width, height: p.image.height, alt: p.image.alt }))

  // Deal photos across the columns in turn so the wall reads left → right, row by row,
  // while each photo keeps its own proportions.
  const columnCount = useColumnCount()
  const columns = Array.from({ length: columnCount }, () => [])
  state.items.forEach((p, i) => columns[i % columnCount].push({ ...p, index: i }))

  return (
    <div className="portfolio-wall">
      <h1 className="visually-hidden">Portfolio</h1>

      {stories?.length > 0 && <PortfolioTabs storyCount={stories.length} photoCount={state.total} galleryRef={galleryHead} />}

      <PortfolioStories stories={stories ?? []} />

      {stories?.length > 0 && (
        <header ref={galleryHead} id="gallery" className="portfolio-wall__head">
          <p className="stories__eyebrow">The gallery</p>
          <h2 className="stories__title">Moments From Every Wedding</h2>
        </header>
      )}

      {state.total === 0 && <p className="empty-note portfolio-wall__empty">{t('portfolio.empty')}</p>}

      <div className="portfolio-wall__grid" style={{ '--cols': columnCount }}>
        {columns.map((col, c) => (
          <ul key={c} className="portfolio-wall__col" role="list">
            {col.map((p) => (
              <WallItem key={p.image.id} delay={c * 120}>
                <button type="button" className="portfolio-wall__btn" onClick={() => setViewer(p.index)} aria-label={`${p.image.alt} — open full screen`}>
                  <ResponsiveImage image={p.image} sizes="(min-width: 900px) 33vw, (min-width: 560px) 50vw, 100vw" priority={p.index < 3} />
                </button>
              </WallItem>
            ))}
          </ul>
        ))}
      </div>

      {state.error && <ErrorState error={state.error} onRetry={loadMore} />}
      {state.loading && <Spinner />}
      <div ref={sentinel} aria-hidden="true" />

      <Lightbox open={viewer >= 0} slides={slides} index={Math.max(viewer, 0)} onClose={() => setViewer(-1)} onIndexChange={setViewer} />
    </div>
  )
}
