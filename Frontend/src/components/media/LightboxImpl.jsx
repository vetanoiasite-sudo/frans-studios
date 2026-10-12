import Lightbox from 'yet-another-react-lightbox'
import Zoom from 'yet-another-react-lightbox/plugins/zoom'
import Video from 'yet-another-react-lightbox/plugins/video'
import Counter from 'yet-another-react-lightbox/plugins/counter'
import 'yet-another-react-lightbox/styles.css'
import 'yet-another-react-lightbox/plugins/counter.css'

/**
 * Full-screen viewer (keyboard, swipe, pinch-zoom, video). Loaded on demand via ./Lightbox.jsx.
 * slides: [{ src, width, height, alt, download? } | { type: 'video', sources: [{ src, type }], poster }]
 */
export default function LightboxImpl({ slides, index, onClose, onIndexChange, renderToolbar }) {
  return (
    <Lightbox
      open
      close={onClose}
      index={index}
      slides={slides}
      plugins={[Zoom, Video, Counter]}
      on={{ view: ({ index: i }) => onIndexChange?.(i) }}
      carousel={{ finite: false, preload: 2, padding: 0 }}
      animation={{ fade: 250, swipe: 300 }}
      zoom={{ maxZoomPixelRatio: 3, scrollToZoom: true }}
      video={{ controls: true, playsInline: true, preload: 'metadata' }}
      controller={{ closeOnBackdropClick: true }}
      styles={{ container: { backgroundColor: 'rgba(14, 15, 12, 0.97)' } }}
      toolbar={renderToolbar ? { buttons: [renderToolbar(), 'close'] } : undefined}
      labels={{ Close: 'Close', Next: 'Next', Previous: 'Previous' }}
    />
  )
}
