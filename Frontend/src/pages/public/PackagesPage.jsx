import '@fontsource/dm-sans/400.css'
import { useApi } from '../../hooks/useApi.js'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { usePageCover } from '../../hooks/usePageCover.js'
import { ResponsiveImage } from '../../components/media/ResponsiveImage.jsx'
import { InquiryForm } from '../../components/public/InquiryForm.jsx'
import { SocialIcon, MailIcon } from '../../components/public/FooterIcons.jsx'
import { studio } from '../../content/site.js'

// Hero photo: uploaded in Admin → Page covers. Until then, a file named "hero" (hero.jpg / .webp) in
// src/assets/packages/ is used if present, otherwise a plain background.
const heroFiles = import.meta.glob('../../assets/packages/hero.{jpg,jpeg,webp,png,avif}', { eager: true, query: '?url', import: 'default' })
const heroPhoto = Object.values(heroFiles)[0]

function formatPrice(p) {
  if (p.price == null) return p.priceNote
  const amount = new Intl.NumberFormat('en', { style: p.currency ? 'currency' : 'decimal', currency: p.currency ?? undefined, maximumFractionDigits: 0 }).format(p.price)
  return [p.priceNote, amount].filter(Boolean).join(' ')
}

/**
 * Packages (structure of the live fransstudios.com/packages-2):
 * full-width photo under the transparent header → white form card on the right that starts halfway
 * down the photo and runs into a #F5F5F5 band, whose left side holds the intro and contact details.
 * Package tiers appear in the band only once the studio publishes them in Admin → Packages.
 */
export default function PackagesPage() {
  usePageTitle('Packages')
  const cover = usePageCover('packages')
  const { data: packages } = useApi('/api/public/packages')

  return (
    <div className="packages-page">
      <h1 className="visually-hidden">Packages</h1>

      <div className="packages-hero" aria-hidden={cover || heroPhoto ? undefined : true}>
        {cover ? (
          <ResponsiveImage image={cover} className="packages-hero__img" sizes="100vw" priority />
        ) : (
          cover === null && heroPhoto && <img className="packages-hero__img" src={heroPhoto} alt="" fetchPriority="high" decoding="async" />
        )}
      </div>

      <div className="packages-band">
        <div className="packages-band__inner">
          <section className="packages-card" aria-labelledby="packages-form-title">
            <h2 id="packages-form-title" className="packages-card__title">
              Ready to Discover Our Packages?
            </h2>
            <p className="packages-card__lead">Save the moments you want to keep forever</p>
            <InquiryForm />
          </section>

          <section className="packages-info" aria-labelledby="packages-info-title">
            <h2 id="packages-info-title" className="packages-info__title">
              Where Your Journey Begins
            </h2>
            <p className="packages-info__text">
              We invite you to complete the form below, and our dedicated team will respond promptly to assist you. Alternatively, you’re welcome to contact
              us directly via email for any immediate inquiries.
            </p>

            <div className="packages-info__group">
              <h3 className="packages-info__label">Inquiries</h3>
              <a className="packages-info__link" href={`mailto:${studio.email}`}>
                <MailIcon />
                {studio.email}
              </a>
            </div>
            <div className="packages-info__group">
              <h3 className="packages-info__label">Follow</h3>
              <a className="packages-info__link" href={studio.instagramHref} target="_blank" rel="noopener noreferrer">
                <SocialIcon name="Instagram" />
                Instagram
              </a>
            </div>
          </section>

          {packages?.length > 0 && (
            <div className="packages-tiers">
              {packages.map((p) => (
                <article key={p.id} className="packages-tier">
                  <h3 className="packages-tier__name">{p.name}</h3>
                  {formatPrice(p) && <p className="packages-tier__price">{formatPrice(p)}</p>}
                  {p.summary && <p className="packages-tier__summary">{p.summary}</p>}
                  {p.features.length > 0 && (
                    <ul className="packages-tier__features">
                      {p.features.map((f) => (
                        <li key={f}>{f}</li>
                      ))}
                    </ul>
                  )}
                </article>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
