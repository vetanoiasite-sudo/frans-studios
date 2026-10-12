import { useState } from 'react'

/**
 * Endless, slowly drifting strip of portrait photos under the hero text.
 * - The list is repeated so the track is always wider than the screen, then rendered twice so a
 *   CSS translate of -50% loops seamlessly (no JavaScript animation loop).
 * - Pauses on hover; keyboard users get a pause button (WCAG 2.2.2); "reduce motion" stops it.
 *
 * @param {{ images: { src: string, alt: string, width: number, height: number }[] }} props
 */
export function HeroCarousel({ images }) {
  const [paused, setPaused] = useState(false)
  if (!images.length) return null

  // Enough copies that one set is ≥ ~2000px wide at desktop tile sizes.
  const repeats = Math.max(1, Math.ceil(9 / images.length))
  const set = Array.from({ length: repeats }, () => images).flat()

  return (
    <div className="hero-carousel" data-paused={paused}>
      <ul className="hero-carousel__track" style={{ '--count': set.length }}>
        {[0, 1].map((copy) =>
          set.map((img, i) => {
            const decorative = copy === 1 || i >= images.length // only the first occurrence is announced
            return (
              <li key={`${copy}-${i}`} className="hero-carousel__item" aria-hidden={decorative || undefined}>
                <img src={img.src} width={img.width} height={img.height} alt={decorative ? '' : img.alt} loading={i < 8 && copy === 0 ? 'eager' : 'lazy'} decoding="async" draggable="false" />
              </li>
            )
          }),
        )}
      </ul>
      <button type="button" className="hero-carousel__toggle" onClick={() => setPaused((p) => !p)} aria-pressed={paused}>
        {paused ? 'Play photo carousel' : 'Pause photo carousel'}
      </button>
    </div>
  )
}
