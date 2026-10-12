import { Link } from 'react-router'

// Drop the photo in src/assets/home/ named "explore" (explore.jpg / .webp / .png) — picked up automatically.
const photos = import.meta.glob('../../assets/home/explore.{jpg,jpeg,webp,png,avif}', { eager: true, query: '?url', import: 'default' })
const photo = Object.values(photos)[0]

const links = [
  { to: '/packages', title: 'See Our Offers', label: 'Packages' },
  { to: '/about', title: 'Discover Our Story', label: 'About Us' },
  { to: '/contact', title: 'Get in Touch', label: 'Contact Us' },
]

/**
 * Home "See Our Offers / Discover Our Story / Get in Touch" section.
 * Figma (1440): inside the 1200px content width; three links on the left spread over the photo's
 * height; photo ≈ 675 × 472 on the right, ending at the content edge.
 */
export function ExploreLinks() {
  return (
    <section className="explore-links" aria-label="Explore Frans Studios">
      <ul className="explore-links__list" role="list">
        {links.map((l) => (
          <li key={l.to}>
            <Link to={l.to} className="explore-link">
              <span className="explore-link__title">{l.title}</span>
              <span className="explore-link__label">{l.label}</span>
            </Link>
          </li>
        ))}
      </ul>
      <div className="explore-links__media">
        {photo ? (
          <img src={photo} width={674} height={470} alt="The bride with her bridesmaids on the wedding morning" loading="lazy" decoding="async" />
        ) : (
          <div className="explore-links__placeholder" aria-hidden="true" />
        )}
      </div>
    </section>
  )
}
