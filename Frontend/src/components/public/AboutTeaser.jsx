import '@fontsource/neuton/300.css'
import '@fontsource/cormorant-garamond/700.css'
import { Link } from 'react-router'

// Drop the team photo in src/assets/about/ named "team" (team.jpg / team.webp / team.png).
// It's picked up automatically; until then a neutral placeholder keeps the layout intact.
const photos = import.meta.glob('../../assets/about/team.{jpg,jpeg,webp,png,avif}', { eager: true, query: '?url', import: 'default' })
const teamPhoto = Object.values(photos)[0]

/** Home page "Hey, we're Frans Studios" block — Figma: 1200 × 698 grey panel, photo 520 × 615. */
export function AboutTeaser() {
  return (
    <section className="about-teaser" aria-labelledby="about-teaser-title">
      <div className="about-teaser__panel">
        <div className="about-teaser__media">
          {teamPhoto ? (
            <img src={teamPhoto} width={519} height={616} alt="The Frans Studios team" loading="lazy" decoding="async" />
          ) : (
            <div className="about-teaser__placeholder" aria-hidden="true" />
          )}
        </div>
        <div className="about-teaser__text">
          <h2 id="about-teaser-title" className="about-teaser__title">
            Hey, we’re Frans Studios— <span className="about-teaser__nowrap">your visual storytellers.</span>
          </h2>
          <p className="about-teaser__body">
            At Frans Studios, photography and cinematography are more than just our profession—they’re our passion. With a keen eye for detail and a
            dedication to capturing every moment’s true essence, we believe that every story deserves to be told with creativity and heart. From the first
            glance to the final frame, we focus on what truly matters: creating memories that will last a lifetime.
          </p>
          <Link to="/about" className="cta-dark about-teaser__cta">
            See Who We Are
          </Link>
        </div>
      </div>
    </section>
  )
}
