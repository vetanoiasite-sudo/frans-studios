import '@fontsource/dm-sans/400.css'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { usePageCover } from '../../hooks/usePageCover.js'
import { ResponsiveImage } from '../../components/media/ResponsiveImage.jsx'
import { InquiryForm } from '../../components/public/InquiryForm.jsx'
import { SocialIcon, MailIcon } from '../../components/public/FooterIcons.jsx'
import { faq, studio } from '../../content/site.js'

// Hero photo: uploaded in Admin → Page covers. Until then, a file named "hero" (hero.jpg / .webp) in
// src/assets/contact/ is used if present, otherwise a plain background.
const heroFiles = import.meta.glob('../../assets/contact/hero.{jpg,jpeg,webp,png,avif}', { eager: true, query: '?url', import: 'default' })
const heroPhoto = Object.values(heroFiles)[0]

function PinIcon() {
  return (
    <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M12 21s-6.5-5.6-6.5-11a6.5 6.5 0 0 1 13 0c0 5.4-6.5 11-6.5 11z" />
      <circle cx="12" cy="10" r="2.4" />
    </svg>
  )
}

/**
 * Contact (structure of the live fransstudios.com/contact-us): the Packages layout — photo, white form
 * card overlapping it, #F5F5F5 band with the intro on the left — followed by the FAQ accordion and a
 * "Let’s Connect" band.
 */
export default function ContactPage() {
  usePageTitle('Contact')
  const cover = usePageCover('contact')

  return (
    <div className="packages-page contact-page">
      <h1 className="visually-hidden">Contact Frans Studios</h1>

      <div className="packages-hero contact-hero" aria-hidden={cover || heroPhoto ? undefined : true}>
        {cover ? (
          <ResponsiveImage image={cover} className="packages-hero__img" sizes="100vw" priority />
        ) : (
          cover === null && heroPhoto && <img className="packages-hero__img" src={heroPhoto} alt="" fetchPriority="high" decoding="async" />
        )}
      </div>

      <div className="packages-band">
        <div className="packages-band__inner">
          <section className="packages-card" aria-labelledby="contact-form-title">
            <h2 id="contact-form-title" className="packages-card__title">
              Let’s Talk
            </h2>
            <p className="packages-card__lead">Have questions or need more details? Fill out the form below.</p>
            <InquiryForm variant="contact" />
          </section>

          <section className="packages-info" aria-labelledby="contact-info-title">
            <h2 id="contact-info-title" className="packages-info__title">
              This is where all the magic happens!
            </h2>
            <p className="packages-info__text">
              Please take a moment to fill out the form, and we will get back to you as soon as possible. Alternatively, feel free to reach out to us via
              email.
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
        </div>
      </div>

      <section className="contact-faq" aria-labelledby="faq-title">
        <h2 id="faq-title" className="contact-faq__title">
          Frequently <br />
          Asked <br />
          Questions
        </h2>
        <div className="contact-faq__list">
          {faq.map((item, i) => (
            // Shared name = only one answer open at a time (like the live accordion); the first starts open.
            <details key={item.q} className="contact-faq__item" name="faq" open={i === 0}>
              <summary className="contact-faq__q">{item.q}</summary>
              <div className="contact-faq__a">
                {item.a.map((p) => (
                  <p key={p}>{p}</p>
                ))}
              </div>
            </details>
          ))}
        </div>
      </section>

      <section className="contact-connect" aria-labelledby="connect-title">
        <h2 id="connect-title" className="contact-connect__title">
          Let’s Connect
        </h2>
        <p className="contact-connect__lead">Capturing moments that words can’t.</p>
        <div className="contact-connect__place">
          <PinIcon />
          <div>
            <p className="contact-connect__country">Egypt</p>
            <a className="contact-connect__phone" href={studio.phoneHref}>
              {studio.phoneFooter}
            </a>
          </div>
        </div>
      </section>
    </div>
  )
}
