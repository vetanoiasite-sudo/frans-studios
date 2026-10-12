import { useEffect, useRef, useState } from 'react'
import { api, refreshPublicCache, upload } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { ErrorBanner, PageHeader, StatusBadge } from '../../components/admin/AdminUi.jsx'
import { formatDateTime } from '../../utils/format.js'
import aboutDefault from '../../assets/about/about-hero.webp'

/** Below this width a full-width cover looks soft or pixelated on large screens. */
const MIN_COVER_WIDTH = 2400

const pages = {
  packages: { label: 'Packages', path: '/packages', fallback: null },
  contact: { label: 'Contact', path: '/contact', fallback: null },
  about: { label: 'About us', path: '/about', fallback: aboutDefault },
}

/**
 * One page's cover: shows what the page uses now, lets the admin pick a new photo, preview it,
 * then save (upload) or cancel. "Use default" removes the uploaded cover.
 */
function CoverCard({ cover, onChanged }) {
  const page = pages[cover.page]
  const input = useRef(null)
  const [file, setFile] = useState(null)
  const [preview, setPreview] = useState(null)
  const [previewWidth, setPreviewWidth] = useState(null)
  const [progress, setProgress] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    setPreviewWidth(null)
    if (!file) return setPreview(null)
    const url = URL.createObjectURL(file)
    setPreview(url)
    return () => URL.revokeObjectURL(url)
  }, [file])

  function cancel() {
    setFile(null)
    setError(null)
    if (input.current) input.current.value = ''
  }

  async function save() {
    setError(null)
    const form = new FormData()
    form.append('file', file)
    try {
      await upload(`/api/admin/page-covers/${cover.page}`, form, setProgress)
      await refreshPublicCache('/api/public/page-covers')
      cancel()
      onChanged()
    } catch (err) {
      setError(err)
    } finally {
      setProgress(null)
    }
  }

  async function useDefault() {
    const message = page.fallback ? 'Go back to the default cover for this page?' : 'Remove this cover? The page will show a plain background until a new one is uploaded.'
    if (!window.confirm(message)) return
    setError(null)
    try {
      await api(`/api/admin/page-covers/${cover.page}`, { method: 'DELETE' })
      await refreshPublicCache('/api/public/page-covers')
      onChanged()
    } catch (err) {
      setError(err)
    }
  }

  const current = cover.image ? `${cover.image.src}/${cover.image.widths.find((w) => w >= 960) ?? cover.image.widths.at(-1)}.webp` : page.fallback
  const shown = preview ?? current
  // Width of the photo being previewed, or of the uploaded cover (stored width = original, capped at 3200).
  const width = preview ? previewWidth : cover.image?.width
  const tooSmall = width != null && width < MIN_COVER_WIDTH

  return (
    <section className="adm-card adm-cover">
      <header className="adm-card__head">
        <h2>{page.label}</h2>
        {preview ? <StatusBadge tone="warn">Not saved yet</StatusBadge> : cover.image ? <StatusBadge tone="ok">Custom cover</StatusBadge> : <StatusBadge>{page.fallback ? 'Default cover' : 'No cover'}</StatusBadge>}
      </header>
      <ErrorBanner error={error} />

      <div className="adm-cover__frame">{shown ? <img src={shown} alt="" onLoad={preview ? (e) => setPreviewWidth(e.currentTarget.naturalWidth) : undefined} /> : <span>No cover yet. The page shows a plain background.</span>}</div>

      {tooSmall && (
        <p className="adm-warning adm-small" role="status">
          {preview ? 'This photo' : 'The current cover'} is only <strong>{width} px</strong> wide, so it will look blurry or pixelated across a large
          screen. Use the original from the camera or Lightroom, at least {MIN_COVER_WIDTH} px wide (not a copy from WhatsApp, Instagram or a
          web search).
        </p>
      )}

      <p className="adm-muted adm-small">
        {cover.updatedAt && !preview ? `Updated ${formatDateTime(cover.updatedAt)} · ` : ''}
        <a href={page.path} target="_blank" rel="noopener noreferrer">
          View page ↗
        </a>
      </p>

      <input ref={input} type="file" accept="image/jpeg,image/png,image/webp" hidden onChange={(e) => setFile(e.target.files[0] ?? null)} />
      <div className="adm-form__actions">
        {preview ? (
          <>
            <button type="button" className="adm-btn adm-btn--primary" onClick={save} disabled={progress !== null}>
              {progress !== null ? `Uploading… ${Math.round(progress * 100)}%` : 'Save cover'}
            </button>
            <button type="button" className="adm-btn" onClick={cancel} disabled={progress !== null}>
              Cancel
            </button>
          </>
        ) : (
          <>
            <button type="button" className="adm-btn adm-btn--primary" onClick={() => input.current?.click()}>
              {shown ? 'Change cover' : 'Upload cover'}
            </button>
            {cover.image && (
              <button type="button" className="adm-btn adm-btn--danger-quiet" onClick={useDefault}>
                {page.fallback ? 'Use default' : 'Remove'}
              </button>
            )}
          </>
        )}
      </div>
    </section>
  )
}

export default function PageCoversPage() {
  const { data, error, loading, reload } = useApi('/api/admin/page-covers')

  return (
    <>
      <PageHeader
        title="Page covers"
        subtitle="The large photo at the top of each page. Landscape photos work best (at least 2400 px wide). JPEG/PNG/WebP — resized and stripped of location data automatically."
      />
      {loading && <Spinner />}
      {error && <ErrorState error={error} onRetry={reload} />}
      {data && (
        <div className="adm-covers">
          {data.filter((c) => pages[c.page]).map((c) => (
            <CoverCard key={c.page} cover={c} onChanged={reload} />
          ))}
        </div>
      )}
    </>
  )
}
