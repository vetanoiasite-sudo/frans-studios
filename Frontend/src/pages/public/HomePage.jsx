import '@fontsource/cormorant-garamond/500-italic.css'
import '@fontsource/cormorant-garamond/700.css'
import { useRef } from 'react'
import { Link } from 'react-router'
import { useApi } from '../../hooks/useApi.js'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { HeroCarousel } from '../../components/public/HeroCarousel.jsx'
import { AboutTeaser } from '../../components/public/AboutTeaser.jsx'
import { FeaturedAlbums } from '../../components/public/FeaturedAlbums.jsx'
import { FeaturedFilms } from '../../components/public/FeaturedFilms.jsx'
import { ExploreLinks } from '../../components/public/ExploreLinks.jsx'
import { stats } from '../../content/site.js'
import { t } from '../../i18n/index.js'
import { gsap, media, playOnce, useGSAP } from '../../animation/gsap.js'
import { revealUp } from '../../animation/reveal.js'
import heroBg from '../../assets/hero/hero-bg.jpg'
import carousel1 from '../../assets/hero/carousel-1.jpg'
import carousel2 from '../../assets/hero/carousel-2.jpg'
import carousel3 from '../../assets/hero/carousel-3.jpg'

const heroPhotos = [
  { src: carousel1, width: 221, height: 312, alt: 'Bride and groom in a close embrace among the trees' },
  { src: carousel2, width: 221, height: 312, alt: 'Bride holding her bouquet on a garden path' },
  { src: carousel3, width: 221, height: 312, alt: 'Bride holding the groom close in a garden' },
]

export default function HomePage() {
  usePageTitle('Vetanoia — Wedding Photography & Cinematography')
  const { data: galleries } = useApi('/api/public/galleries')
  const { data: films } = useApi('/api/public/films')

  // Albums marked "Featured" in Admin → Portfolio (in their sort order); otherwise the latest published ones.
  const featured = galleries?.filter((g) => g.isFeatured) ?? []
  const stories = (featured.length ? featured : galleries ?? []).slice(0, 6)

  const heroRef = useRef(null)
  const statsRef = useRef(null)

  // Opening: the photo settles from a slow zoom while the words arrive line by line, then the strip of
  // photos rises into place. On desktop the background then drifts slower than the page while scrolling.
  useGSAP(
    () => {
      const mm = gsap.matchMedia()
      mm.add({ ok: media.ok, desktop: media.desktop }, ({ conditions }) => {
        if (!conditions.ok) return
        // Below 1100px the lines wrap as one paragraph (inline), so the title moves as a whole.
        const lines = window.matchMedia('(min-width: 1100px)').matches ? '.home-hero__line' : '.home-hero__title'
        gsap
          .timeline()
          .fromTo('.home-hero__bg', { scale: 1.14 }, { scale: 1, duration: 2.8, ease: 'expo.out' }, 0)
          .from('.home-hero__eyebrow', { autoAlpha: 0, y: 14, duration: 1.1 }, 0.3)
          .from(lines, { autoAlpha: 0, y: 22, duration: 1.2, stagger: 0.14 }, 0.5)
          .from('.home-hero__cta', { autoAlpha: 0, y: 16, duration: 0.9 }, '-=0.75')
          .from('.hero-carousel', { autoAlpha: 0, y: 70, duration: 1.5, ease: 'expo.out' }, '-=0.8')

        if (conditions.desktop) {
          const scroll = { trigger: heroRef.current, start: 'top top', end: 'bottom top', scrub: 0.6 }
          gsap.to('.home-hero__bg', { yPercent: 12, ease: 'none', scrollTrigger: scroll })
          gsap.to('.home-hero__content', { yPercent: -16, autoAlpha: 0.15, ease: 'none', scrollTrigger: { ...scroll, end: '70% top' } })
        }
      })
    },
    { scope: heroRef },
  )

  // The numbers count up once, as the row comes into view.
  useGSAP(
    () => {
      const mm = gsap.matchMedia()
      mm.add(media.ok, () => {
        revealUp('.home-stats__item', { y: 16, stagger: 0.1, start: 'top 90%' })
        gsap.utils.toArray('[data-count]', statsRef.current).forEach((el) => {
          const target = Number(el.dataset.count)
          const rest = el.dataset.rest
          // "1M +" counts 0.0 → 1.0 rather than jumping from 0 to 1.
          const decimals = target < 5 && rest.trim() ? 1 : 0
          const value = { n: 0 }
          const write = () => (el.textContent = `${value.n.toFixed(decimals)}${rest}`)
          write()
          gsap.to(value, {
            n: target,
            duration: 2,
            ease: 'power2.out',
            onUpdate: write,
            onComplete: () => (el.textContent = el.dataset.value),
            scrollTrigger: { trigger: el, start: 'top 92%', toggleActions: playOnce },
          })
        })
      })
    },
    { scope: statsRef },
  )

  return (
    <>
      <section ref={heroRef} className="home-hero" aria-labelledby="home-title">
        <img className="home-hero__bg" src={heroBg} width={1440} height={1046} alt="" fetchPriority="high" decoding="async" />
        <div className="home-hero__content">
          <span className="home-hero__eyebrow">Love. Art. Captured</span>
          <h1 id="home-title" className="home-hero__title">
            <span className="home-hero__line">Celebrate Your Unique Love Story By Capturing </span>
            <span className="home-hero__line">Every Detail With A Touch Of Sophistication </span>
            <span className="home-hero__line">And Timeless Beauty</span>
          </h1>
          <Link to="/contact" className="home-hero__cta">
            {t('home.cta')}
          </Link>
        </div>
        <HeroCarousel images={heroPhotos} />
      </section>

      <section ref={statsRef} className="home-stats" aria-label="Vetanoia in numbers">
        <dl className="home-stats__list">
          {stats.map((s) => (
            <div key={s.label} className="home-stats__item">
              <dt className="home-stats__label">{s.label}</dt>
              <dd className="home-stats__value">
                <StatValue value={s.value} />
              </dd>
            </div>
          ))}
        </dl>
      </section>

      <AboutTeaser />

      <FeaturedAlbums albums={stories} />

      <FeaturedFilms films={films ?? []} />

      <ExploreLinks />
    </>
  )
}

/**
 * "1200 +" → the number that counts up (hidden from screen readers, which only hear the final value).
 * Values without a leading number are shown as they are.
 */
function StatValue({ value }) {
  const match = /^(\d+(?:\.\d+)?)(.*)$/.exec(value)
  if (!match) return value
  return (
    <>
      <span aria-hidden="true" data-count={match[1]} data-rest={match[2]} data-value={value}>
        {value}
      </span>
      <span className="visually-hidden">{value}</span>
    </>
  )
}
