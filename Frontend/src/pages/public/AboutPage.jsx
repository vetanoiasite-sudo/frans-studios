import '@fontsource/pinyon-script/400.css'
import '@fontsource/neuton/300.css'
import '@fontsource/cormorant-garamond/700.css'
import { useRef } from 'react'
import { Link } from 'react-router'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { usePageCover } from '../../hooks/usePageCover.js'
import { ResponsiveImage } from '../../components/media/ResponsiveImage.jsx'
import { aboutTestimonial, founder } from '../../content/site.js'
import { MeetOurFamily } from '../../components/public/MeetOurFamily.jsx'
import { gsap, media, useGSAP } from '../../animation/gsap.js'
import { revealImage, revealUp } from '../../animation/reveal.js'
import heroPhoto from '../../assets/about/about-hero.webp'
import founderPhoto from '../../assets/about/founder.jpg'

export default function AboutPage() {
  usePageTitle('About Us')
  const cover = usePageCover('about') // Admin → Page covers; the built-in team photo until one is uploaded
  const ref = useRef(null)

  // The team photo settles from a slow zoom (and drifts slower than the page on desktop); the quote
  // and its signature follow; the founder's portrait is uncovered with the text arriving beside it.
  // The cover comes from the API, so this re-runs once it is known.
  useGSAP(
    () => {
      if (cover === undefined) return
      const mm = gsap.matchMedia()
      mm.add({ ok: media.ok, desktop: media.desktop }, ({ conditions }) => {
        if (!conditions.ok) return
        gsap.fromTo('.about-hero__img', { scale: 1.12 }, { scale: 1, duration: 2.6, ease: 'expo.out' })
        if (conditions.desktop) {
          gsap.to('.about-hero__img', {
            yPercent: 10,
            ease: 'none',
            scrollTrigger: { trigger: '.about-hero', start: 'top top', end: 'bottom top', scrub: 0.6 },
          })
        }
        revealUp('.about-quote__text, .about-quote__signature, .about-quote__label', { stagger: 0.14 })
        revealImage(ref.current.querySelector('.about-founder__media'), { start: 'top 78%' })
        revealUp('.about-founder__title, .about-founder__body > p, .about-founder__signature, .about-founder__cta', {
          stagger: 0.1,
          start: 'top 85%',
        })
      })
    },
    { scope: ref, dependencies: [cover === undefined], revertOnUpdate: true },
  )

  return (
    <div ref={ref}>
      {/* Hero — Figma: full-width 1440 × 1024 team photo; the header sits over it. */}
      <section className="about-hero">
        <h1 className="visually-hidden">About Vetanoia</h1>
        {cover && <ResponsiveImage image={cover} className="about-hero__img" sizes="100vw" priority alt="" />}
        {cover === null && (
          <img
            className="about-hero__img"
            src={heroPhoto}
            width={1440}
            height={1024}
            alt="The Vetanoia team dressed in black in a white studio, some sitting and some standing"
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
    </div>
  )
}
