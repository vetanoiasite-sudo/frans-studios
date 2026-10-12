/**
 * Renders a public portfolio image (from /media) as <picture> with WebP + JPEG srcsets.
 * Width/height are always set so the browser reserves space (no layout shift).
 *
 * @param {{ image: { src: string, width: number, height: number, widths: number[], alt: string }, sizes?: string, priority?: boolean, className?: string }} props
 */
export function ResponsiveImage({ image, sizes = '100vw', priority = false, className = '', alt }) {
  if (!image) return null
  const set = (ext) => image.widths.map((w) => `${image.src}/${w}.${ext} ${w}w`).join(', ')
  const fallbackWidth = image.widths.find((w) => w >= 960) ?? image.widths[image.widths.length - 1]
  return (
    <picture className={className}>
      <source type="image/webp" srcSet={set('webp')} sizes={sizes} />
      <img
        src={`${image.src}/${fallbackWidth}.jpg`}
        srcSet={set('jpg')}
        sizes={sizes}
        width={image.width}
        height={image.height}
        alt={alt ?? image.alt ?? ''}
        loading={priority ? 'eager' : 'lazy'}
        decoding="async"
        fetchPriority={priority ? 'high' : undefined}
      />
    </picture>
  )
}

/** Largest variant URL of a public image (for the lightbox). */
export function largestSrc(image, ext = 'webp') {
  return `${image.src}/${image.widths[image.widths.length - 1]}.${ext}`
}
