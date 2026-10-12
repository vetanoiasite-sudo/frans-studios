import '@fontsource/cormorant-garamond/500-italic.css'
import '@fontsource/cormorant-garamond/700.css'
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
import heroBg from '../../assets/hero/hero-bg.jpg'
import carousel1 from '../../assets/hero/carousel-1.jpg'
import carousel2 from '../../assets/hero/carousel-2.jpg'
import carousel3 from '../../assets/hero/carousel-3.jpg'

const heroPhotos = [
  { src: carousel1, width: 221, height: 312, alt: 'Bride and groom sharing a kiss' },
  { src: carousel2, width: 220, height: 312, alt: 'Bride smiling over her shoulder' },
  { src: carousel3, width: 220, height: 312, alt: 'Bride and groom embracing in a garden' },
]

export default function HomePage() {
  usePageTitle('Frans Studios — Wedding Photography & Cinematography')
  const { data: galleries } = useApi('/api/public/galleries')
  const { data: films } = useApi('/api/public/films')

  // Albums marked "Featured" in Admin → Portfolio (in their sort order); otherwise the latest published ones.
  const featured = galleries?.filter((g) => g.isFeatured) ?? []
  const stories = (featured.length ? featured : galleries ?? []).slice(0, 6)

  return (
    <>
      <section className="home-hero" aria-labelledby="home-title">
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

      <section className="home-stats" aria-label="Frans Studios in numbers">
        <dl className="home-stats__list">
          {stats.map((s) => (
            <div key={s.label} className="home-stats__item">
              <dt className="home-stats__label">{s.label}</dt>
              <dd className="home-stats__value">{s.value}</dd>
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
