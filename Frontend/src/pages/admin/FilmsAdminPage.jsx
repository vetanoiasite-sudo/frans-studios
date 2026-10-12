import { useEffect, useRef, useState } from 'react'
import { api, upload } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { Dialog } from '../../components/ui/Dialog.jsx'
import { Checkbox, Field } from '../../components/ui/Field.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { ErrorBanner, PageHeader, StatusBadge } from '../../components/admin/AdminUi.jsx'
import { formatBytes } from '../../utils/format.js'

function FilmEditor({ film, isNew, onChanged }) {
  const [form, setForm] = useState({ title: film.title, description: film.description ?? '', isPublished: film.isPublished, sortOrder: film.sortOrder })
  const [error, setError] = useState(null)
  const [notice, setNotice] = useState(null)
  const [progress, setProgress] = useState(null)
  const [saving, setSaving] = useState(false)
  const card = useRef(null)

  // A film that was just created is added at the end of the list: bring it into view.
  useEffect(() => {
    if (isNew) card.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [isNew])

  const set = (name) => (e) => setForm((f) => ({ ...f, [name]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }))

  // Save is only offered when the form differs from what's saved (the server trims the texts).
  const changed =
    form.title.trim() !== film.title ||
    form.description.trim() !== (film.description ?? '') ||
    form.isPublished !== film.isPublished ||
    (Number(form.sortOrder) || 0) !== film.sortOrder

  async function save(e) {
    e.preventDefault()
    setError(null)
    setNotice(null)
    setSaving(true)
    try {
      await api(`/api/admin/films/${film.id}`, { method: 'PUT', body: { ...form, sortOrder: Number(form.sortOrder) || 0 } })
      setNotice(
        form.isPublished
          ? 'Saved. The film is on the website (it can take up to a minute to appear there).'
          : 'Saved. The film is hidden from the website: tick “Published” and save again to show it.',
      )
      onChanged()
    } catch (err) {
      setError(err)
    } finally {
      setSaving(false)
    }
  }

  async function send(kind, file) {
    if (!file) return
    setError(null)
    setNotice(null)
    const form = new FormData()
    form.append('file', file)
    try {
      await upload(`/api/admin/films/${film.id}/${kind}`, form, (p) => setProgress({ kind, p }))
      setNotice(kind === 'video' ? 'Video uploaded.' : 'Poster uploaded.')
      onChanged()
    } catch (err) {
      setError(err)
    } finally {
      setProgress(null)
    }
  }

  return (
    <form ref={card} className="adm-card adm-form" onSubmit={save}>
      <header className="adm-card__head">
        <h2>{film.title}</h2>
        {film.isPublished ? <StatusBadge tone="ok">Published</StatusBadge> : <StatusBadge>Hidden</StatusBadge>}
      </header>
      <ErrorBanner error={error} />
      {notice && <p className="form-success">{notice}</p>}
      <div className="adm-film">
        <div className="adm-film__media">
          {film.videoUrl ? (
            <video key={film.videoUrl} controls preload="none" poster={film.poster ? `${film.poster.src}/${film.poster.widths[0]}.jpg` : undefined} src={film.videoUrl} />
          ) : (
            <div className="adm-film__empty">No video yet</div>
          )}
        </div>
        <div className="adm-form">
          <Field label="Title" required value={form.title} onChange={set('title')} />
          <Field label="Description" as="textarea" rows={3} value={form.description} onChange={set('description')} />
          <Field label="Sort order" type="number" value={form.sortOrder} onChange={set('sortOrder')} />
          <Checkbox label="Published" checked={form.isPublished} onChange={set('isPublished')} />
        </div>
      </div>
      <div className="adm-form__row">
        <label className="adm-dropzone adm-dropzone--small">
          <input type="file" accept="image/*" onChange={(e) => send('poster', e.target.files[0])} disabled={Boolean(progress)} />
          <span>{progress?.kind === 'poster' ? `Uploading ${Math.round(progress.p * 100)}%` : film.poster ? 'Replace poster image' : 'Upload poster image'}</span>
        </label>
        <label className="adm-dropzone adm-dropzone--small">
          <input type="file" accept="video/mp4,video/webm" onChange={(e) => send('video', e.target.files[0])} disabled={Boolean(progress)} />
          <span>
            {progress?.kind === 'video'
              ? `Uploading ${Math.round(progress.p * 100)}%`
              : film.videoUrl
                ? `Replace video (${formatBytes(film.videoBytes)})`
                : 'Upload web video (MP4)'}
          </span>
        </label>
      </div>
      <div className="adm-form__actions">
        <button type="submit" className="adm-btn adm-btn--primary" disabled={!changed || saving}>
          {saving ? 'Saving…' : 'Save'}
        </button>
        <button
          type="button"
          className="adm-btn adm-btn--danger-quiet"
          onClick={() => window.confirm(`Delete “${film.title}” from the website?`) && api(`/api/admin/films/${film.id}`, { method: 'DELETE' }).then(onChanged)}
        >
          Delete
        </button>
      </div>
    </form>
  )
}

export default function FilmsAdminPage() {
  const { data, error, loading, reload } = useApi('/api/admin/films')
  const [creating, setCreating] = useState(false)
  const [title, setTitle] = useState('')
  const [saveError, setSaveError] = useState(null)
  const [createdId, setCreatedId] = useState(null)

  async function create(e) {
    e.preventDefault()
    setSaveError(null)
    try {
      const film = await api('/api/admin/films', {
        method: 'POST',
        body: { title: title.trim(), description: null, isPublished: false, sortOrder: (data?.length ?? 0) * 10 },
      })
      setCreating(false)
      setTitle('')
      setCreatedId(film.id)
      reload()
    } catch (err) {
      setSaveError(err)
    }
  }

  return (
    <>
      <PageHeader
        title="Films"
        subtitle="The first 4 published films appear on the home page. A poster alone is enough to publish (shown as “Film coming soon”); add the web video later (1080p H.264 MP4 with “fast start”, ideally under ~300 MB). Masters belong in the client’s Drive folder, not here."
        actions={
          <button type="button" className="adm-btn adm-btn--primary" onClick={() => setCreating(true)}>
            + New film
          </button>
        }
      />
      {loading && <Spinner />}
      {error && <ErrorState error={error} onRetry={reload} />}
      {data?.length === 0 && <p className="adm-muted">No films yet.</p>}
      <div className="adm-stack">
        {data?.map((f) => (
          <FilmEditor key={f.id} film={f} isNew={f.id === createdId} onChanged={reload} />
        ))}
      </div>

      <Dialog
        open={creating}
        onClose={() => setCreating(false)}
        title="New film"
        footer={
          <button type="submit" form="new-film" className="adm-btn adm-btn--primary">
            Create &amp; add the video
          </button>
        }
      >
        <form id="new-film" className="adm-form" onSubmit={create}>
          <ErrorBanner error={saveError} />
          <p className="adm-muted adm-small">Step 1 of 2: name the film. You’ll upload its poster and video next.</p>
          <Field label="Title" placeholder="Sara & Amr — Wedding Film" required maxLength={200} value={title} onChange={(e) => setTitle(e.target.value)} />
        </form>
      </Dialog>
    </>
  )
}
