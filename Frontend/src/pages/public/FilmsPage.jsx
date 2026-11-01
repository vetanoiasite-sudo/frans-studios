import { useRef } from 'react'
import { useFilms } from '../../hooks/useFilms.js'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { VideoTile } from '../../components/media/VideoTile.jsx'
import { Spinner } from '../../components/ui/Spinner.jsx'
import { gsap, media, ScrollTrigger, useGSAP } from '../../animation/gsap.js'
import { revealImage, revealUp } from '../../animation/reveal.js'

/** Poster with player chrome; the video file only downloads when the visitor presses play. */
function Film({ film }) {
  return (
    <article className="film">
      <VideoTile film={film} sizes="(min-width: 1200px) 1100px, 100vw" />
      <div className="film__meta">
        <h2 className="film__title">{film.title}</h2>
        {film.description && <p className="film__desc">{film.description}</p>}
      </div>
    </article>
  )
}

export default function FilmsPage() {
  usePageTitle('Wedding Films')
  // Admin → Films when any are published, otherwise the built-in films (never an empty page).
  const { data, loading } = useFilms()
  const ref = useRef(null)
  const count = data?.length ?? 0

  // The heading arrives first, once, as the page opens.
  useGSAP(
    () => {
      const mm = gsap.matchMedia()
      mm.add(media.ok, () => revealUp('.page-head > *', { stagger: 0.12 }))
    },
    { scope: ref },
  )

  // Each film's player is uncovered as it scrolls into view, with its title and description following.
  // Films come from the API, so this re-runs when they arrive.
  useGSAP(
    () => {
      const mm = gsap.matchMedia()
      mm.add(media.ok, () => {
        gsap.utils.toArray('.film', ref.current).forEach((film) => {
          revealImage(film.querySelector('.video-tile'), { start: 'top 85%' })
          revealUp(film.querySelectorAll('.film__title, .film__desc'), { stagger: 0.1, start: 'top 95%' })
        })
      })
      if (!count) return
      // The list grew from nothing when the films came in: re-measure every trigger on the page, on the
      // next frame rather than while these triggers are still being created.
      const frame = requestAnimationFrame(() => ScrollTrigger.refresh())
      return () => cancelAnimationFrame(frame)
    },
    { scope: ref, dependencies: [count], revertOnUpdate: true },
  )

  return (
    <div ref={ref} className="pub-page">
      <header className="page-head">
        <p className="eyebrow">Cinematography</p>
        <h1 className="page-head__title">Wedding films</h1>
        <p className="page-head__lead">Through the lens of cinematic artistry we craft films that narrate your love story.</p>
      </header>
      {loading && <Spinner />}
      <div className="film-list">{data?.map((f) => <Film key={f.id} film={f} />)}</div>
    </div>
  )
}
