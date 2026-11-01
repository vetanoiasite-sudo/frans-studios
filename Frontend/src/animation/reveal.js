import { gsap, playOnce, ScrollTrigger } from './gsap.js'

// Reveal helpers shared by the home page sections. Call them inside a gsap.matchMedia(media.ok)
// handler so they are skipped (and reverted) when the visitor prefers reduced motion.

/**
 * Elements rise a little and fade in as they reach the viewport. Elements entering together are
 * batched into one staggered tween rather than one tween each.
 * @param {Element[]|string} targets
 */
export function revealUp(targets, { y = 22, stagger = 0.09, start = 'top 88%' } = {}) {
  const els = gsap.utils.toArray(targets)
  if (!els.length) return
  gsap.set(els, { autoAlpha: 0, y })
  ScrollTrigger.batch(els, {
    start,
    onEnter: (batch) => gsap.to(batch, { autoAlpha: 1, y: 0, duration: 1, stagger, overwrite: true }),
  })
}

/**
 * A photo frame opens from the bottom edge while the photo inside settles from a slight zoom — the
 * feel of a print being uncovered. The photo's inline transform is cleared afterwards so the CSS
 * hover zoom keeps working.
 * @param {Element} frame the element that clips the photo (overflow: hidden)
 */
export function revealImage(frame, { start = 'top 82%', delay = 0 } = {}) {
  if (!frame) return
  const photo = frame.querySelector('img')
  const tl = gsap.timeline({ delay, scrollTrigger: { trigger: frame, start, toggleActions: playOnce } })
  tl.fromTo(frame, { clipPath: 'inset(100% 0% 0% 0%)' }, { clipPath: 'inset(0% 0% 0% 0%)', duration: 1.4, ease: 'expo.out', clearProps: 'clipPath' })
  if (photo) tl.fromTo(photo, { scale: 1.16 }, { scale: 1, duration: 1.8, ease: 'expo.out', clearProps: 'transform' }, 0)
  return tl
}
