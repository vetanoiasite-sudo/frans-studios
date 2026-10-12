import { useState } from 'react'
import { largestSrc, ResponsiveImage } from './ResponsiveImage.jsx'

const Icon = {
  play: (
    <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true">
      <path d="M7 4.5v15l12-7.5z" fill="currentColor" />
    </svg>
  ),
  next: (
    <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true">
      <path d="M5 5v14l10-7zM17 5h2.5v14H17z" fill="currentColor" />
    </svg>
  ),
  volume: (
    <svg viewBox="0 0 24 24" width="15" height="15" aria-hidden="true">
      <path d="M4 9.5h4L13 5v14l-5-4.5H4z" fill="currentColor" />
      <path d="M16 8.5a5 5 0 0 1 0 7M18.5 6a8.5 8.5 0 0 1 0 12" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  ),
  settings: (
    <svg viewBox="0 0 24 24" width="15" height="15" aria-hidden="true">
      <path
        d="M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7zm8 3.5-.1 1.3 2 1.6-2 3.4-2.4-.9a7.6 7.6 0 0 1-2.2 1.3L15 21h-4l-.4-2.6a7.6 7.6 0 0 1-2.2-1.3l-2.4.9-2-3.4 2-1.6a7.8 7.8 0 0 1 0-2.6l-2-1.6 2-3.4 2.4.9a7.6 7.6 0 0 1 2.2-1.3L11 3h4l.4 2.6a7.6 7.6 0 0 1 2.2 1.3l2.4-.9 2 3.4-2 1.6c.1.4.1.8.1 1.3z"
        fill="currentColor"
      />
    </svg>
  ),
  fullscreen: (
    <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true">
      <path d="M4 9V4h5M15 4h5v5M20 15v5h-5M9 20H4v-5" fill="none" stroke="currentColor" strokeWidth="2" />
    </svg>
  ),
}

/**
 * A film shown as a video player (Figma "video design"): poster, round play button, control bar.
 * When the film has a video, pressing play swaps in a real <video> with native controls (the
 * file only downloads at that moment). Without a video yet it shows "Film coming soon".
 *
 * @param {{ film: { title: string, poster: object|null, videoUrl: string|null, videoMimeType?: string }, sizes?: string }} props
 */
export function VideoTile({ film, sizes = '100vw' }) {
  const [playing, setPlaying] = useState(false)
  const [notice, setNotice] = useState(false)
  const hasVideo = Boolean(film.videoUrl)

  if (playing && hasVideo) {
    return (
      <div className="video-tile video-tile--playing">
        <video className="video-tile__video" controls autoPlay playsInline preload="auto" poster={film.poster ? largestSrc(film.poster, 'jpg') : undefined}>
          <source src={film.videoUrl} type={film.videoMimeType ?? 'video/mp4'} />
        </video>
      </div>
    )
  }

  return (
    <div className="video-tile">
      {film.poster ? <ResponsiveImage image={film.poster} alt={film.poster.alt || film.title} sizes={sizes} /> : <div className="video-tile__placeholder" />}

      <button
        type="button"
        className="video-tile__play"
        aria-label={hasVideo ? `Play film: ${film.title}` : `${film.title} — film coming soon`}
        onClick={() => (hasVideo ? setPlaying(true) : setNotice(true))}
        onBlur={() => setNotice(false)}
      >
        <span className="video-tile__play-circle" aria-hidden="true">
          <svg viewBox="0 0 24 24" width="26" height="26">
            <path d="M8 5.5v13l10.5-6.5z" fill="currentColor" />
          </svg>
        </span>
        {!hasVideo && (
          <span className="video-tile__soon" data-show={notice} role="status">
            Film coming soon
          </span>
        )}
      </button>

      {/* Decorative player chrome from the design; the real controls appear once the video plays. */}
      <div className="video-tile__bar" aria-hidden="true">
        <span className="video-tile__progress" />
        <span className="video-tile__controls">
          <span className="video-tile__group">
            {Icon.play}
            {Icon.next}
            {Icon.volume}
          </span>
          <span className="video-tile__group">
            <span className="video-tile__settings">
              {Icon.settings}
              <span className="video-tile__hd">HD</span>
            </span>
            {Icon.fullscreen}
          </span>
        </span>
      </div>
    </div>
  )
}
