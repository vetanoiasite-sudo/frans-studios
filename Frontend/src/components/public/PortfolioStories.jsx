import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ResponsiveImage } from '../media/ResponsiveImage.jsx'
import { formatDate } from '../../utils/format.js'
import { t } from '../../i18n/index.js'

function ArrowIcon() {
  return (
    <svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.3">
      <path d="M2 8h11M9 4l4 4-4 4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

function ChevronIcon({ flip }) {
  return (
    <svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.4" style={flip ? { transform: 'scaleX(-1)' } : undefined}>
      <path d="M6 3l5 5-5 5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

/**
 * "Wedding stories" band at the top of the Portfolio page: one card per published story
 * (Admin → Portfolio → Stories), each opening that wedding's own page. Category filter chips appear
 * only when the stories span more than one category. The cards sit in one sideways row
 * (arrows on wider screens) so the photo gallery below stays close, however many stories there are.
 *
 * @param {{ stories: { slug: string, title: string, category: string, location: string|null, eventDate: string|null, cover: object|null, imageCount: number }[] }} props
 */
export function PortfolioStories({ stories }) {
  const [category, setCategory] = useState('all')
  const list = useRef(null)
  const [edges, setEdges] = useState({ start: true, end: true })

  // Show the arrows only when there is more to scroll to in that direction.
  useEffect(() => {
    const el = list.current
    if (!el) return
    const update = () => {
      const max = el.scrollWidth - el.clientWidth
      const x = Math.abs(el.scrollLeft)
      setEdges({ start: x <= 2, end: x >= max - 2 })
    }
    update()
    el.addEventListener('scroll', update, { passive: true })
    window.addEventListener('resize', update)
    return () => {
      el.removeEventListener('scroll', update)
      window.removeEventListener('resize', update)
    }
  }, [stories, category])

  if (!stories.length) return null

  const categories = [...new Set(stories.map((s) => s.category))]
  const shown = category === 'all' ? stories : stories.filter((s) => s.category === category)
  const rtl = typeof document !== 'undefined' && document.documentElement.dir === 'rtl'
  const scroll = (dir) => list.current?.scrollBy({ left: dir * (rtl ? -1 : 1) * list.current.clientWidth * 0.9, behavior: 'smooth' })

  return (
    <section id="stories" className="stories" aria-labelledby="stories-title">
      <header className="stories__head">
        <p className="stories__eyebrow">Wedding stories</p>
        <h2 id="stories-title" className="stories__title">
          Step Inside Every Celebration
        </h2>
        <p className="stories__lead">Each story is one couple’s day, told from beginning to end. Choose a story to see their wedding in full.</p>

        {categories.length > 1 && (
          <div className="stories__filters" role="group" aria-label="Filter stories">
            {['all', ...categories].map((c) => (
              <button key={c} type="button" className="stories__chip" aria-pressed={category === c} onClick={() => setCategory(c)}>
                {t(c === 'all' ? 'portfolio.all' : `portfolio.category.${c}`)}
              </button>
            ))}
          </div>
        )}
      </header>

      <div className="stories__rail">
        <ul ref={list} className="stories__list" role="list">
          {shown.map((s, i) => {
            const meta = [s.location, s.eventDate && formatDate(s.eventDate, { month: 'long', year: 'numeric' })].filter(Boolean).join(' · ')
            return (
              <li key={s.slug} className="stories__item">
                <Link to={`/portfolio/${s.slug}`} className="story-card">
                  <div className="story-card__media">
                    {s.cover ? (
                      <ResponsiveImage image={s.cover} alt="" sizes="(min-width: 960px) 30vw, (min-width: 600px) 46vw, 80vw" priority={i < 3} />
                    ) : (
                      <div className="story-card__placeholder" />
                    )}
                    <span className="story-card__count">{t('portfolio.photos', { count: s.imageCount })}</span>
                  </div>
                  <div className="story-card__body">
                    {meta && <p className="story-card__meta">{meta}</p>}
                    <h3 className="story-card__title">{s.title}</h3>
                    <span className="story-card__cta">
                      View the story <ArrowIcon />
                    </span>
                  </div>
                </Link>
              </li>
            )
          })}
        </ul>
        {!(edges.start && edges.end) && (
          <>
            <button type="button" className="stories__arrow stories__arrow--prev" onClick={() => scroll(-1)} disabled={edges.start} aria-label="Previous stories">
              <ChevronIcon flip={!rtl} />
            </button>
            <button type="button" className="stories__arrow stories__arrow--next" onClick={() => scroll(1)} disabled={edges.end} aria-label="More stories">
              <ChevronIcon flip={rtl} />
            </button>
          </>
        )}
      </div>
    </section>
  )
}
