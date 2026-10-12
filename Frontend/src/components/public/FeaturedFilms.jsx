import { Link } from 'react-router'
import { VideoTile } from '../media/VideoTile.jsx'

/**
 * Home "Bring Your Unique Love Story to the Screen" section. Figma: 1200 × 1121; centred heading,
 * text and button; 2 × 2 grid of 594 × 402 video tiles with 12px gaps.
 * Films come from Admin → Films (poster now, video whenever it's uploaded).
 *
 * @param {{ films: object[] }} props
 */
export function FeaturedFilms({ films }) {
  if (!films.length) return null
  return (
    <section className="featured-films" aria-labelledby="featured-films-title">
      <header className="featured-films__head">
        <h2 id="featured-films-title" className="featured-films__title">
          Bring Your Unique Love Story to the Screen
        </h2>
        <p className="featured-films__lead">
          Through the lens of cinematic artistry, we craft films that narrate your love story. Every glance, every laugh, and every embrace is woven into
          a visual masterpiece.
        </p>
        <Link to="/films" className="cta-dark featured-films__cta">
          Explore Our Wedding Stories
        </Link>
      </header>
      <ul className="featured-films__grid" role="list">
        {films.slice(0, 4).map((film) => (
          <li key={film.id}>
            <VideoTile film={film} sizes="(min-width: 1240px) 594px, (min-width: 720px) 50vw, 100vw" />
          </li>
        ))}
      </ul>
    </section>
  )
}
