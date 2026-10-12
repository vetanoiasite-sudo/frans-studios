import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useApi } from '../../hooks/useApi.js'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { largestSrc, ResponsiveImage } from '../../components/media/ResponsiveImage.jsx'
import { Lightbox } from '../../components/media/Lightbox.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import NotFoundPage from './NotFoundPage.jsx'
import { formatDate } from '../../utils/format.js'
import { t } from '../../i18n/index.js'

export default function GalleryPage() {
  const { slug } = useParams()
  const { data: g, error, loading, reload } = useApi(`/api/public/galleries/${encodeURIComponent(slug)}`)
  const [index, setIndex] = useState(-1)
  usePageTitle(g?.title)

  if (loading) return <Spinner />
  if (error?.status === 404) return <NotFoundPage />
  if (error) return <ErrorState error={error} onRetry={reload} />
  if (!g) return null

  const slides = g.images.map((img) => ({ src: largestSrc(img), width: img.width, height: img.height, alt: img.alt }))

  return (
    <article className="gallery">
      <header className="gallery__head">
        <p className="eyebrow">
          {[g.location, g.eventDate && formatDate(g.eventDate, { month: 'long', year: 'numeric' })].filter(Boolean).join(' · ') || 'Love story'}
        </p>
        <h1 className="gallery__title">{g.title}</h1>
        {g.intro && <p className="gallery__intro">{g.intro}</p>}
        <p className="gallery__count">{t('portfolio.photos', { count: g.images.length })}</p>
      </header>

      <ul className="masonry" role="list">
        {g.images.map((img, i) => (
          <li key={img.id ?? i} className="masonry__item">
            <button type="button" className="masonry__btn" onClick={() => setIndex(i)} aria-label={`${img.alt} — open full screen`}>
              <ResponsiveImage image={img} sizes="(min-width: 1100px) 33vw, (min-width: 640px) 50vw, 100vw" priority={i < 2} />
            </button>
          </li>
        ))}
      </ul>

      <nav className="gallery__pager" aria-label="More stories">
        {g.previous ? (
          <Link to={`/portfolio/${g.previous.slug}`} className="gallery__pager-link">
            <span className="eyebrow">{t('portfolio.previous')}</span>
            <span className="gallery__pager-title">{g.previous.title}</span>
          </Link>
        ) : (
          <span />
        )}
        {g.next && (
          <Link to={`/portfolio/${g.next.slug}`} className="gallery__pager-link gallery__pager-link--next">
            <span className="eyebrow">{t('portfolio.next')}</span>
            <span className="gallery__pager-title">{g.next.title}</span>
          </Link>
        )}
      </nav>

      <Lightbox open={index >= 0} slides={slides} index={Math.max(index, 0)} onClose={() => setIndex(-1)} />
    </article>
  )
}
