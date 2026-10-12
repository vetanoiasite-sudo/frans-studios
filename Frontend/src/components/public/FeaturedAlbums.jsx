import { Link } from 'react-router'
import { ResponsiveImage } from '../media/ResponsiveImage.jsx'

function ArrowIcon() {
  return (
    <svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.3">
      <path d="M2 8h11M9 4l4 4-4 4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

/**
 * Home "Every Detail, Every Emotion…" section. Figma: 1200 wide, 3 × 2 grid of album cards
 * (392 × 640: 600px photo + 40px caption row), 12px column gap, ~48px row gap.
 * Albums are the admin-managed portfolio stories (Admin → Portfolio); each has many photos —
 * the card shows its cover and links to the full album page.
 *
 * @param {{ albums: { slug: string, title: string, cover: object|null }[] }} props
 */
export function FeaturedAlbums({ albums }) {
  if (!albums.length) return null
  return (
    <section className="featured-albums" aria-labelledby="featured-albums-title">
      <header className="featured-albums__head">
        <div>
          <h2 id="featured-albums-title" className="featured-albums__title">
            Every Detail, Every Emotion, <span className="featured-albums__line">Perfectly Preserved Forever</span>
          </h2>
          <p className="featured-albums__lead">
            We focus on what matters most—your unforgettable moments—blending heartfelt storytelling with creative visuals to preserve their beauty and
            meaning.
          </p>
        </div>
        <Link to="/portfolio" className="cta-dark featured-albums__cta">
          Explore The Gallery
        </Link>
      </header>

      <ul className="featured-albums__grid" role="list">
        {albums.map((album, i) => (
          <li key={album.slug}>
            <Link to={`/portfolio/${album.slug}`} className="album-card">
              <div className="album-card__media">
                {album.cover ? (
                  <ResponsiveImage image={album.cover} alt={album.cover.alt || album.title} sizes="(min-width: 1240px) 392px, (min-width: 960px) 32vw, (min-width: 600px) 48vw, 100vw" priority={i < 3} />
                ) : (
                  <div className="album-card__placeholder" />
                )}
              </div>
              <span className="album-card__caption">
                {album.title}
                <ArrowIcon />
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}
