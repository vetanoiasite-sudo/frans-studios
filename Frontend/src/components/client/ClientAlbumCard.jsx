import { Link } from 'react-router'
import { formatDate } from '../../utils/format.js'
import { t } from '../../i18n/index.js'

/** Summary line of an album: "12 March 2026 · 340 photos · 3 videos". */
export function albumSummary(a) {
  return [
    a.eventDate && formatDate(a.eventDate),
    a.photoCount > 0 && t('client.album.photos', { count: a.photoCount }),
    a.videoCount > 0 && t('client.album.videos', { count: a.videoCount }),
  ]
    .filter(Boolean)
    .join(' · ')
}

/**
 * One gallery as a large landscape card: the cover photo with the title set on it, the date and counts
 * below. `compact` is the smaller version used for "Your other galleries" under an album.
 */
export function ClientAlbumCard({ album, compact = false }) {
  return (
    <Link to={`/client/albums/${album.id}`} className={`cl-card${compact ? ' cl-card--compact' : ''}`}>
      <div className="cl-card__media">
        {album.coverFileId ? (
          <img src={`/api/client/files/${album.coverFileId}/thumbnail?size=${compact ? 'medium' : 'large'}`} alt="" loading="lazy" decoding="async" />
        ) : (
          // No photo to use as cover (e.g. a films-only gallery): a quiet panel with a film or photo mark.
          <div className="cl-card__placeholder" aria-hidden="true">
            <svg viewBox="0 0 24 24" width="40" height="40">
              {album.videoCount > 0 && album.photoCount === 0 ? (
                <path d="M4 6h11v12H4zM15 10l5-3v10l-5-3" fill="none" stroke="currentColor" strokeWidth="1.2" strokeLinejoin="round" />
              ) : (
                <path d="M4 6h16v12H4zM4 15l4-4 4 4 3-3 5 5M15 9.5a1 1 0 1 0 0 .01" fill="none" stroke="currentColor" strokeWidth="1.2" strokeLinejoin="round" />
              )}
            </svg>
          </div>
        )}
        <div className="cl-card__overlay">
          <h2 className="cl-card__title">{album.title}</h2>
        </div>
      </div>
      <div className="cl-card__meta">
        <span className="cl-card__summary">{albumSummary(album)}</span>
        <span className="cl-card__cta">
          {t('client.album.open')} <span aria-hidden="true">→</span>
        </span>
      </div>
    </Link>
  )
}
