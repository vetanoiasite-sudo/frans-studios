import { useRef } from 'react'
import { Link } from 'react-router'
import { gsap, media, ScrollTrigger, useGSAP } from '../../animation/gsap.js'
import { revealUp } from '../../animation/reveal.js'
import { VideoTile } from '../media/VideoTile.jsx'

/**
 * Home "Bring Your Unique Love Story to the Screen" section. Figma: 1200 × 1121; centred heading,
 * text and button; 2 × 2 grid of 594 × 402 video tiles with 12px gaps.
 * Films come from Admin → Films (poster now, video whenever it's uploaded).
 *
 * @param {{ films: object[] }} props
 */
export function FeaturedFilms({ films }) {
  const ref = useRef(null)

  // Centred heading first, then the four films settle in from a slightly smaller size.
  useGSAP(
    () => {
      if (!films.length) return
      const mm = gsap.matchMedia()
      mm.add(media.ok, () => {
        revealUp('.featured-films__head > *', { stagger: 0.1 })
        const tiles = gsap.utils.toArray('.featured-films__grid > li', ref.current)
        gsap.set(tiles, { autoAlpha: 0, y: 40, scale: 0.96 })
        ScrollTrigger.batch(tiles, {
          start: 'top 88%',
          onEnter: (batch) =>
            gsap.to(batch, { autoAlpha: 1, y: 0, scale: 1, duration: 1.2, ease: 'expo.out', stagger: 0.12, overwrite: true, clearProps: 'transform' }),
        })
      })
      // The section grew from nothing when the data came in: re-measure every trigger on the page, on the
      // next frame rather than while this section's own triggers are still being created.
      const frame = requestAnimationFrame(() => ScrollTrigger.refresh())
      return () => cancelAnimationFrame(frame)
    },
    { scope: ref, dependencies: [films.length], revertOnUpdate: true },
  )

  if (!films.length) return null
  return (
    <section ref={ref} className="featured-films" aria-labelledby="featured-films-title">
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
