import '@fontsource/neuton/300.css'
import '@fontsource/neuton/400.css'
import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, ScrollRestoration, useLocation } from 'react-router'
import { t } from '../i18n/index.js'
import { copyrightHolder, footerContacts, socials, studio } from '../content/site.js'
import { ContactIcon, SocialIcon } from '../components/public/FooterIcons.jsx'
import logoBlack from '../assets/brand/logo-black.png'
import logoWhite from '../assets/brand/logo-white.png'
import '../styles/public.css'

// Order follows the Figma header.
const links = [
  { to: '/', label: t('nav.home'), end: true },
  { to: '/about', label: t('nav.about') },
  { to: '/films', label: t('nav.films') },
  { to: '/portfolio', label: t('nav.portfolio') },
  { to: '/contact', label: t('nav.contact') },
  { to: '/packages', label: t('nav.packages') },
  { to: '/client', label: t('nav.login') },
]

function InstagramIcon() {
  return (
    <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.6">
      <rect x="3" y="3" width="18" height="18" rx="5" />
      <circle cx="12" cy="12" r="4.2" />
      <circle cx="17.3" cy="6.7" r="0.9" fill="currentColor" stroke="none" />
    </svg>
  )
}

export default function PublicLayout() {
  const location = useLocation()
  // Pages whose top is a full-bleed photo: the header starts transparent over it.
  const overHero = ['/', '/about', '/packages', '/contact'].includes(location.pathname.replace(/\/$/, '') || '/')
  const [menuOpen, setMenuOpen] = useState(false)

  useEffect(() => setMenuOpen(false), [location.pathname])

  useEffect(() => {
    document.body.style.overflow = menuOpen ? 'hidden' : ''
    return () => {
      document.body.style.overflow = ''
    }
  }, [menuOpen])

  return (
    <>
      <a className="skip-link" href="#main">
        {t('nav.skip')}
      </a>
      {/* Same translucent dark bar on every page, as on the live site. */}
      <header className="pub-header">
        <div className="pub-header__inner">
          <Link to="/" className="pub-logo" aria-label="Vetanoia — home">
            <img className="pub-logo__img" src={logoWhite} width={1974} height={300} alt="" />
          </Link>

          <nav className="pub-nav" aria-label="Main">
            {links.map((l) => (
              <NavLink key={l.to} to={l.to} end={l.end} className="pub-nav__link">
                {l.label}
              </NavLink>
            ))}
            <a href={studio.instagramHref} className="pub-nav__icon" target="_blank" rel="noopener noreferrer" aria-label="Instagram">
              <InstagramIcon />
            </a>
          </nav>

          <div className="pub-header__end">
            <button
              type="button"
              className="pub-menu-btn"
              aria-expanded={menuOpen}
              aria-controls="mobile-menu"
              onClick={() => setMenuOpen((o) => !o)}
            >
              <span className="visually-hidden">{menuOpen ? t('nav.close') : t('nav.menu')}</span>
              <span className="pub-menu-btn__bar" aria-hidden="true" />
              <span className="pub-menu-btn__bar" aria-hidden="true" />
              <span className="pub-menu-btn__bar" aria-hidden="true" />
            </button>
            <a href={studio.instagramHref} className="pub-header__insta" target="_blank" rel="noopener noreferrer" aria-label="Instagram">
              <InstagramIcon />
            </a>
          </div>
        </div>

        <nav id="mobile-menu" className="pub-mobile-nav" data-open={menuOpen} aria-label="Main" hidden={!menuOpen}>
          {links.map((l) => (
            <NavLink key={l.to} to={l.to} end={l.end} className="pub-mobile-nav__link">
              {l.label}
            </NavLink>
          ))}
          <a href={studio.instagramHref} className="pub-mobile-nav__client" target="_blank" rel="noopener noreferrer">
            Instagram {studio.instagramHandle}
          </a>
        </nav>
      </header>

      <main id="main" tabIndex={-1} className={overHero ? 'pub-main pub-main--hero' : 'pub-main'}>
        <Outlet />
      </main>

      {/* Footer (latest design): #F5F5F5 — logo, then about text | contacts | social icons; black credit bar. */}
      <footer className="pub-footer">
        <div className="pub-footer__inner">
          <Link to="/" className="pub-footer__logo" aria-label="Vetanoia — home">
            <img src={logoBlack} width={1974} height={300} alt="" />
          </Link>
          <p className="pub-footer__about">{studio.footerAbout}</p>
          <address className="pub-footer__contact">
            {footerContacts.map((c) => (
              <a key={c.icon} href={c.href} {...(c.external ? { target: '_blank', rel: 'noopener noreferrer' } : {})}>
                <ContactIcon name={c.icon} />
                {c.label}
              </a>
            ))}
          </address>
          <ul className="pub-footer__social" role="list" aria-label="Vetanoia on social media">
            {socials
              .filter((s) => s.href)
              .map((s) => (
                <li key={s.name}>
                  <a href={s.href} target="_blank" rel="noopener noreferrer" aria-label={s.name}>
                    <SocialIcon name={s.name} />
                  </a>
                </li>
              ))}
          </ul>
        </div>
        <p className="pub-footer__credit">
          © {new Date().getFullYear()} {copyrightHolder}. All rights reserved.
        </p>
      </footer>
      <ScrollRestoration />
    </>
  )
}
