// GSAP setup for the public site: plugins registered once, the motion rules every section follows.
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'
import { useGSAP } from '@gsap/react'

gsap.registerPlugin(ScrollTrigger, useGSAP)

// Slow, decelerating motion suits the editorial look; sections override only where a moment needs it.
gsap.defaults({ ease: 'power3.out', duration: 0.9 })

// Every animation runs inside gsap.matchMedia() under `ok`, so a visitor whose device asks for reduced
// motion gets the finished page with nothing moving. Content is only ever hidden by GSAP (never by
// CSS), so the page is complete without JavaScript and for crawlers too.
export const media = {
  ok: '(prefers-reduced-motion: no-preference)',
  desktop: '(min-width: 960px)',
}

// Reveals play once and then stay. Not `once: true`: that kills the trigger as it fires, and when it
// fires during a refresh (content arriving from the API, fonts loading) the trigger list changes
// under ScrollTrigger's feet and it throws.
export const playOnce = 'play none none none'

export { gsap, ScrollTrigger, useGSAP }
