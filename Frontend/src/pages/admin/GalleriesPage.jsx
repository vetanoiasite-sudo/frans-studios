import { useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { api, upload } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { Dialog } from '../../components/ui/Dialog.jsx'
import { Field } from '../../components/ui/Field.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { ErrorBanner, PageHeader, StatusBadge } from '../../components/admin/AdminUi.jsx'

export const slugify = (s) =>
  s
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/&/g, ' ')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 120)

/**
 * "Upload photos" → straight onto the public Portfolio wall (newest first). No story needed.
 * Batches go up last-to-first: each batch is placed before the existing photos, so the whole
 * selection ends up at the top in the order it was chosen.
 */
function WallPhotos({ wall, reload, fileInput }) {
  const [progress, setProgress] = useState(null)
  const [error, setError] = useState(null)

  async function uploadFiles(files) {
    if (!files.length) return
    setError(null)
    const list = Array.from(files)
    const batches = []
    for (let i = 0; i < list.length; i += 5) batches.push(list.slice(i, i + 5))
    try {
      for (let b = batches.length - 1, done = 0; b >= 0; b--, done += 5) {
        const form = new FormData()
        batches[b].forEach((f) => form.append('files', f))
        await upload('/api/admin/galleries/wall/images', form, (p) => setProgress({ done, total: list.length, fraction: p }))
      }
    } catch (err) {
      setError(err)
    } finally {
      setProgress(null)
      if (fileInput.current) fileInput.current.value = ''
      reload()
    }
  }

  async function remove(image) {
    if (!window.confirm('Delete this photo from the website?')) return
    await api(`/api/admin/galleries/${wall.id}/images/${image.id}`, { method: 'DELETE' })
    reload()
  }

  return (
    <section className="adm-card">
      <input ref={fileInput} type="file" accept="image/*" multiple hidden onChange={(e) => uploadFiles(e.target.files)} />
      <h2>Portfolio photos ({wall?.images.length ?? 0})</h2>
      <p className="adm-muted adm-small">
        Shown on the Portfolio page, newest first. JPEG/PNG/WebP up to 60 MB each — resized and stripped of location data automatically.
      </p>
      <ErrorBanner error={error} />
      {progress && (
        <p role="status">
          Uploading {Math.min(progress.done + 5, progress.total)} of {progress.total}… {Math.round(progress.fraction * 100)}%
        </p>
      )}
      {wall?.images.length ? (
        <ul className="adm-thumbs" role="list">
          {wall.images.map((img) => (
            <li key={img.id} className="adm-thumb">
              <div className="adm-thumb__img">
                <img src={`${img.image.src}/${img.image.widths[0]}.webp`} alt="" loading="lazy" />
              </div>
              <div className="adm-thumb__actions">
                <button type="button" className="adm-link-btn adm-small adm-text-danger" onClick={() => remove(img)}>
                  Delete
                </button>
              </div>
            </li>
          ))}
        </ul>
      ) : (
        !progress && <p className="adm-muted">No photos yet — click “Upload photos”.</p>
      )}
    </section>
  )
}

export default function GalleriesPage() {
  const navigate = useNavigate()
  const { data, error, loading, reload } = useApi('/api/admin/galleries')
  const { data: wall, reload: reloadWall } = useApi('/api/admin/galleries/wall')
  const fileInput = useRef(null)
  const [creating, setCreating] = useState(false)
  const [form, setForm] = useState({ title: '', slug: '' })
  const [saveError, setSaveError] = useState(null)

  async function create(e) {
    e.preventDefault()
    setSaveError(null)
    try {
      const g = await api('/api/admin/galleries', {
        method: 'POST',
        body: { ...form, category: 'wedding', isPublished: false, isFeatured: false, sortOrder: (data?.length ?? 0) * 10 },
      })
      navigate(`/admin/portfolio/${g.id}`)
    } catch (err) {
      setSaveError(err)
    }
  }

  return (
    <>
      <PageHeader
        title="Portfolio"
        subtitle="Photos on the public Portfolio page. Uploaded here (optimised automatically) — not from Google Drive."
        actions={
          <button type="button" className="adm-btn adm-btn--primary" onClick={() => fileInput.current?.click()}>
            Upload photos
          </button>
        }
      />

      <WallPhotos wall={wall} reload={reloadWall} fileInput={fileInput} />

      <div className="adm-section-head">
        <h2>Stories (optional)</h2>
        <button type="button" className="adm-btn" onClick={() => setCreating(true)}>
          + New story
        </button>
      </div>
      <p className="adm-muted adm-small">Named albums (e.g. “Sara &amp; Amr”) with their own page; featured ones appear on the homepage.</p>
      {loading && <Spinner />}
      {error && <ErrorState error={error} onRetry={reload} />}
      {data && data.length === 0 && <p className="adm-muted">No stories yet.</p>}
      {data && data.length > 0 && (
        <ul className="adm-gallery-grid" role="list">
          {data.map((g) => (
            <li key={g.id}>
              <Link to={`/admin/portfolio/${g.id}`} className="adm-gallery-card">
                <div className="adm-gallery-card__img">{g.cover ? <img src={`${g.cover.src}/${g.cover.widths[0]}.webp`} alt="" loading="lazy" /> : <span>No images</span>}</div>
                <div className="adm-gallery-card__meta">
                  <strong>{g.title}</strong>
                  <span className="adm-muted adm-small">
                    /portfolio/{g.slug} · {g.imageCount} images
                  </span>
                  <span>
                    {g.isPublished ? <StatusBadge tone="ok">Published</StatusBadge> : <StatusBadge>Hidden</StatusBadge>} {g.isFeatured && <StatusBadge tone="warn">Featured</StatusBadge>}
                  </span>
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}

      <Dialog
        open={creating}
        onClose={() => setCreating(false)}
        title="New portfolio story"
        footer={
          <button type="submit" form="new-gallery" className="adm-btn adm-btn--primary">
            Create &amp; add photos
          </button>
        }
      >
        <form id="new-gallery" className="adm-form" onSubmit={create}>
          <ErrorBanner error={saveError} />
          <p className="adm-muted adm-small">Step 1 of 2: name the story. You’ll upload its photos on the next page.</p>
          <Field label="Title" placeholder="Sara & Amr" required value={form.title} onChange={(e) => setForm({ title: e.target.value, slug: slugify(e.target.value) })} />
          <Field
            label="Page address (filled in from the title)"
            hint={`fransstudios.com/portfolio/${form.slug || '…'}`}
            required
            value={form.slug}
            onChange={(e) => setForm({ ...form, slug: e.target.value })}
            error={saveError?.fieldErrors?.slug}
          />
        </form>
      </Dialog>
    </>
  )
}
