import '@fontsource/pinyon-script/400.css'
import { Link } from 'react-router'
import { family } from '../../content/site.js'

// All team photos in src/assets/team/, looked up by file name (e.g. "mina-fathy").
const photos = import.meta.glob('../../assets/team/*.{jpg,jpeg,webp,png}', { eager: true, query: '?url', import: 'default' })
const photoUrl = (name) => Object.entries(photos).find(([path]) => path.split('/').pop().replace(/\.\w+$/, '') === name)?.[1]

/**
 * About page "Meet Our Family". Design: 1200px, 3 columns of 384 × 600 portrait cards, 24px gaps.
 * The first cell holds the title + intro, the last cell a testimonial + "Contact Us" button;
 * names sit in white at the bottom-left of each photo.
 */
export function MeetOurFamily() {
  const { testimonial } = family
  return (
    <section className="family" aria-labelledby="family-title">
      <ul className="family__grid" role="list">
        <li className="family__intro">
          <h2 id="family-title" className="family__title">
            {family.title}
          </h2>
          <p className="family__text">{family.intro}</p>
        </li>

        {family.members.map((m) => (
          <li key={m.name} className="family__member">
            {photoUrl(m.photo) ? (
              <img src={photoUrl(m.photo)} width={384} height={600} alt={`${m.name}, Frans Studios team`} loading="lazy" decoding="async" />
            ) : (
              <div className="family__placeholder" aria-hidden="true" />
            )}
            <span className="family__name">{m.name}</span>
          </li>
        ))}

        <li className="family__quote">
          <figure>
            <blockquote className="family__quote-text">
              <p>“{testimonial.quote}”</p>
            </blockquote>
            <figcaption className="family__quote-name">{testimonial.name}</figcaption>
          </figure>
          <Link to="/contact" className="cta-dark family__cta">
            Contact Us
          </Link>
        </li>
      </ul>
    </section>
  )
}
