import '@fontsource/pinyon-script/400.css'
import '@fontsource/neuton/300.css'
import '@fontsource/cormorant-garamond/700.css'
import { Link } from 'react-router'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { usePageCover } from '../../hooks/usePageCover.js'
import { ResponsiveImage } from '../../components/media/ResponsiveImage.jsx'
import { aboutTestimonial, founder } from '../../content/site.js'
import { MeetOurFamily } from '../../components/public/MeetOurFamily.jsx'
import heroPhoto from '../../assets/about/about-hero.webp'
import founderPhoto from '../../assets/about/founder.jpg'

export default function AboutPage() {
  usePageTitle('About Us')
  const cover = usePageCover('about') // Admin → Page covers; the built-in team photo until one is uploaded
  return (
    <>
      {/* Hero — Figma: full-width 1440 × 1024 team photo; the header sits over it. */}
      <section className="about-hero">
        <h1 className="visually-hidden">About Frans Studios</h1>
        {cover && <ResponsiveImage image={cover} className="about-hero__img" sizes="100vw" priority alt="" />}
        {cover === null && (
          <img
            className="about-hero__img"
            src={heroPhoto}
            width={1440}
            height={1024}
            alt="The Frans Studios team in the studio, some sitting, others walking past in motion blur"
            fetchPriority="high"
            decoding="async"
          />
        )}
      </section>

      {/* Testimonial — centred quote, script signature, small uppercase label. */}
      <section className="about-quote" aria-label="What our couples say">
        <figure className="about-quote__figure">
          <blockquote className="about-quote__text">
            <p>“{aboutTestimonial.quote}”</p>
          </blockquote>
          <figcaption className="about-quote__caption">
            <span className="about-quote__signature">{aboutTestimonial.name}</span>
            <span className="about-quote__label">
              {aboutTestimonial.name} · {aboutTestimonial.role}
            </span>
          </figcaption>
        </figure>
      </section>

      {/* Founder — full-width #F5F5F5 band; tall portrait on the left, text column on the right. */}
      <section className="about-founder" aria-labelledby="founder-title">
        <div className="about-founder__inner">
          <div className="about-founder__media">
            <img src={founderPhoto} width={683} height={1024} alt={`${founder.name} walking through the studio with a camera, smiling`} loading="lazy" decoding="async" />
          </div>
          <div className="about-founder__text">
            <h2 id="founder-title" className="about-founder__title">
              {founder.heading}
            </h2>
            <div className="about-founder__body">
              <p>{founder.lead}</p>
              {founder.paragraphs.map((p) => (
                <p key={p}>{p}</p>
              ))}
            </div>
            <p className="about-founder__signature">{founder.signature}</p>
            <Link to="/portfolio" className="about-founder__cta">
              See Our Work
            </Link>
          </div>
        </div>
      </section>

      <MeetOurFamily />
    </>
  )
}
