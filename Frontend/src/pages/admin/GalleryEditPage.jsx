import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { api, upload } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { Checkbox, Field } from '../../components/ui/Field.jsx'
import { ConfirmDialog } from '../../components/ui/Dialog.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { ErrorBanner, Notice, PageHeader } from '../../components/admin/AdminUi.jsx'

function ImageItem({ galleryId, image, isCover, index, count, onMove, onChanged, onError }) {
  const [alt, setAlt] = useState(image.altText)
  const [state, setState] = useState('idle') // idle | saving | saved | failed
  const [busy, setBusy] = useState(false)

  async function saveAlt() {
    if (alt === image.altText) return
    setState('saving')
    try {
      await api(`/api/admin/galleries/${galleryId}/images/${image.id}`, { method: 'PUT', body: { altText: alt } })
      setState('saved')
    } catch (err) {
      setState('failed')
      onError(err)
    }
  }

  async function act(request) {
    setBusy(true)
    try {
      await request()
      onChanged()
    } catch (err) {
      onError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <li className={isCover ? 'adm-thumb adm-thumb--cover' : 'adm-thumb'}>
      <div className="adm-thumb__img">
        <img src={`${image.image.src}/${image.image.widths[0]}.webp`} alt="" loading="lazy" />
      </div>
      <input className="adm-input adm-input--small" aria-label="Alt text (describes the photo for search engines and screen readers)" placeholder="Describe the photo…" value={alt} onChange={(e) => setAlt(e.target.value)} onBlur={saveAlt} />
      {state === 'saving' && <span className="adm-muted adm-small">Saving…</span>}
      {state === 'saved' && <span className="adm-muted adm-small">Saved</span>}
      {state === 'failed' && <span className="adm-small adm-text-danger">Not saved</span>}
      <div className="adm-thumb__actions">
        <button type="button" className="adm-link-btn" disabled={index === 0} onClick={() => onMove(index, -1)} aria-label="Move earlier">
          ←
        </button>
        <button type="button" className="adm-link-btn" disabled={index === count - 1} onClick={() => onMove(index, 1)} aria-label="Move later">
          →
        </button>
        {isCover ? (
          <span className="adm-thumb__cover">Cover</span>
        ) : (
          <button
            type="button"
            className="adm-link-btn adm-small"
            disabled={busy}
            onClick={() => act(() => api(`/api/admin/galleries/${galleryId}/cover`, { method: 'PUT', body: { imageId: image.id } }))}
          >
            Cover
          </button>
        )}
        <button
          type="button"
          className="adm-link-btn adm-small adm-text-danger"
          disabled={busy}
          onClick={() => window.confirm('Delete this image from the website?') && act(() => api(`/api/admin/galleries/${galleryId}/images/${image.id}`, { method: 'DELETE' }))}
        >
          {busy ? 'Working…' : 'Delete'}
        </button>
      </div>
    </li>
  )
}

export default function GalleryEditPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const { data: g, error, loading, reload, setData } = useApi(`/api/admin/galleries/${id}`)
  const [form, setForm] = useState(null)
  const [saveError, setSaveError] = useState(null)
  const [notice, setNotice] = useState(null)
  const [saving, setSaving] = useState(false)
  const [imagesError, setImagesError] = useState(null)
  const [progress, setProgress] = useState(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const fileInput = useRef(null)

  useEffect(() => {
    if (g)
      setForm({
        title: g.title,
        slug: g.slug,
        category: g.category,
        location: g.location ?? '',
        eventDate: g.eventDate ?? '',
        intro: g.intro ?? '',
        seoDescription: g.seoDescription ?? '',
        isPublished: g.isPublished,
        isFeatured: g.isFeatured,
        sortOrder: g.sortOrder,
      })
  }, [g])

  if (loading) return <Spinner />
  if (error) return <ErrorState error={error} onRetry={reload} />
  if (!g || !form) return null

  const set = (name) => (e) => setForm((f) => ({ ...f, [name]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }))

  async function save(e) {
    e.preventDefault()
    setSaveError(null)
    setNotice(null)
    setSaving(true)
    try {
      await api(`/api/admin/galleries/${id}`, {
        method: 'PUT',
        body: { ...form, location: form.location || null, eventDate: form.eventDate || null, sortOrder: Number(form.sortOrder) || 0 },
      })
      setNotice(form.isPublished ? 'Saved. The story is on the website (it can take up to a minute to appear there).' : 'Saved. The story is hidden from the website.')
      reload()
    } catch (err) {
      setSaveError(err)
    } finally {
      setSaving(false)
    }
  }

  async function uploadFiles(files) {
    if (!files.length) return
    setSaveError(null)
    // Upload in small batches so progress is meaningful and one bad file doesn't lose the rest.
    const list = Array.from(files)
    try {
      for (let i = 0; i < list.length; i += 5) {
        const form = new FormData()
        list.slice(i, i + 5).forEach((f) => form.append('files', f))
        await upload(`/api/admin/galleries/${id}/images`, form, (p) => setProgress({ done: i, total: list.length, fraction: p }))
      }
    } catch (err) {
      setSaveError(err)
    } finally {
      setProgress(null)
      if (fileInput.current) fileInput.current.value = ''
      reload()
    }
  }

  // Shown in the new order right away; put back if the server doesn't save it.
  async function move(index, delta) {
    const before = g
    const images = [...g.images]
    const [item] = images.splice(index, 1)
    images.splice(index + delta, 0, item)
    setData({ ...g, images })
    setImagesError(null)
    try {
      await api(`/api/admin/galleries/${id}/images/order`, { method: 'PUT', body: { ids: images.map((x) => x.id) } })
    } catch (err) {
      setData(before)
      setImagesError(err)
    }
  }

  async function remove() {
    setDeleting(true)
    try {
      await api(`/api/admin/galleries/${id}`, { method: 'DELETE' })
      navigate('/admin/portfolio', { replace: true })
    } catch (err) {
      setSaveError(err)
      setDeleting(false)
      setConfirmDelete(false)
    }
  }

  return (
    <>
      <PageHeader
        title={g.title}
        subtitle={
          <>
            {g.isPublished ? (
              <a href={`/portfolio/${g.slug}`} target="_blank" rel="noopener noreferrer">
                View on website ↗
              </a>
            ) : (
              'Hidden from the website'
            )}
          </>
        }
        back={<Link to="/admin/portfolio" className="adm-back">← Portfolio</Link>}
      />
      <div className="adm-columns">
        <form className="adm-card adm-form" onSubmit={save}>
          <h2>Story details</h2>
          <ErrorBanner error={saveError} />
          <Notice>{notice}</Notice>
          <Field label="Title" required value={form.title} onChange={set('title')} />
          <Field label="Page address" hint={`fransstudios.com/portfolio/${form.slug}`} required value={form.slug} onChange={set('slug')} error={saveError?.fieldErrors?.slug} />
          <div className="adm-form__row">
            <Field label="Category" as="select" value={form.category} onChange={set('category')}>
              <option value="wedding">Wedding</option>
              <option value="engagement">Engagement</option>
              <option value="destination">Destination</option>
              <option value="other">Other</option>
            </Field>
            <Field label="Event date" type="date" value={form.eventDate} onChange={set('eventDate')} />
          </div>
          <Field label="Location" placeholder="Cairo, Egypt" value={form.location} onChange={set('location')} />
          <Field label="Intro text" as="textarea" rows={4} value={form.intro} onChange={set('intro')} />
          <Field label="Search description (optional)" hint="Shown by Google and in link previews. ~150 characters." maxLength={300} value={form.seoDescription} onChange={set('seoDescription')} />
          <div className="adm-form__row">
            <Field label="Sort order" type="number" hint="Lower numbers appear first." value={form.sortOrder} onChange={set('sortOrder')} />
          </div>
          <Checkbox label="Published" hint="Visible on the website." checked={form.isPublished} onChange={set('isPublished')} />
          <Checkbox label="Featured" hint="Shown on the homepage." checked={form.isFeatured} onChange={set('isFeatured')} />
          <div className="adm-form__actions">
            <button type="submit" className="adm-btn adm-btn--primary" disabled={saving}>
              {saving ? 'Saving…' : 'Save'}
            </button>
            <button type="button" className="adm-btn adm-btn--danger-quiet" onClick={() => setConfirmDelete(true)}>
              Delete story
            </button>
          </div>
        </form>

        <section className="adm-card">
          <h2>Add images</h2>
          <p className="adm-muted adm-small">JPEG/PNG/WebP up to 60 MB each. Images are resized, converted to WebP + JPEG and stripped of location data automatically.</p>
          <label className="adm-dropzone">
            <input ref={fileInput} type="file" accept="image/*" multiple onChange={(e) => uploadFiles(e.target.files)} disabled={Boolean(progress)} />
            {progress ? (
              <span>
                Uploading {Math.min(progress.done + 5, progress.total)} of {progress.total}… {Math.round(progress.fraction * 100)}%
              </span>
            ) : (
              <span>Click to choose images</span>
            )}
          </label>
        </section>
      </div>

      <section className="adm-card">
        <h2>Images ({g.images.length})</h2>
        <ErrorBanner error={imagesError} />
        {g.images.length === 0 ? (
          <p className="adm-muted">No images yet.</p>
        ) : (
          <ul className="adm-thumbs" role="list">
            {g.images.map((img, i) => (
              <ImageItem
                key={img.id}
                galleryId={id}
                image={img}
                isCover={g.coverImageId === img.id || (!g.coverImageId && i === 0)}
                index={i}
                count={g.images.length}
                onMove={move}
                onChanged={() => (setImagesError(null), reload())}
                onError={setImagesError}
              />
            ))}
          </ul>
        )}
      </section>

      <ConfirmDialog
        open={confirmDelete}
        title="Delete this story?"
        message="The story and all its uploaded images are removed from the website."
        confirmLabel="Delete"
        danger
        busy={deleting}
        onConfirm={remove}
        onCancel={() => setConfirmDelete(false)}
      />
    </>
  )
}
