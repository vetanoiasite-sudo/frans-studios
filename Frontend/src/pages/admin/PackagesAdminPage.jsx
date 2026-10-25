import { Link } from 'react-router'
import { useState } from 'react'
import { api } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { Checkbox, Field } from '../../components/ui/Field.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { ErrorBanner, Notice, PageHeader, StatusBadge } from '../../components/admin/AdminUi.jsx'

const blank = { name: '', summary: '', price: '', currency: 'EGP', priceNote: '', features: '', isPublished: false, sortOrder: 0 }

function PackageForm({ initial, onSaved, onCancel }) {
  const [form, setForm] = useState(initial ? { ...blank, ...initial, price: initial.price ?? '', currency: initial.currency ?? '', summary: initial.summary ?? '', priceNote: initial.priceNote ?? '', features: initial.features ?? '' } : blank)
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)
  const set = (name) => (e) => setForm((f) => ({ ...f, [name]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }))

  async function save(e) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    const body = {
      ...form,
      price: form.price === '' ? null : Number(form.price),
      currency: form.currency ? form.currency.toUpperCase() : null,
      sortOrder: Number(form.sortOrder) || 0,
    }
    try {
      await api(initial ? `/api/admin/packages/${initial.id}` : '/api/admin/packages', { method: initial ? 'PUT' : 'POST', body })
      onSaved(initial ? `Saved “${form.name}”.` : `Package “${form.name}” added.`)
    } catch (err) {
      setError(err)
      setBusy(false)
    }
  }

  return (
    <form className="adm-card adm-form" onSubmit={save}>
      <ErrorBanner error={error} />
      <Field label="Name" required value={form.name} onChange={set('name')} />
      <Field label="Summary" as="textarea" rows={2} value={form.summary} onChange={set('summary')} />
      <div className="adm-form__row">
        <Field label="Price (optional)" type="number" min="0" step="1" value={form.price} onChange={set('price')} />
        <Field label="Currency" placeholder="EGP" maxLength={3} value={form.currency} onChange={set('currency')} error={error?.fieldErrors?.currency} />
        <Field label="Price note" placeholder="Starting from" value={form.priceNote} onChange={set('priceNote')} />
      </div>
      <Field label="What’s included (one per line)" as="textarea" rows={5} value={form.features} onChange={set('features')} />
      <div className="adm-form__row">
        <Field label="Sort order" type="number" value={form.sortOrder} onChange={set('sortOrder')} />
      </div>
      <Checkbox label="Published" hint="Shown above the inquiry form on the Packages page." checked={form.isPublished} onChange={set('isPublished')} />
      <div className="adm-form__actions">
        <button type="submit" className="adm-btn adm-btn--primary" disabled={busy}>
          {busy ? 'Saving…' : 'Save package'}
        </button>
        <button type="button" className="adm-btn" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
      </div>
    </form>
  )
}

export default function PackagesAdminPage() {
  const { data, error, loading, reload } = useApi('/api/admin/packages')
  const [editing, setEditing] = useState(null) // null | 'new' | package
  const [notice, setNotice] = useState(null)
  const [actionError, setActionError] = useState(null)

  const done = (message) => {
    setEditing(null)
    setNotice(message)
    reload()
  }

  async function remove(p) {
    if (!window.confirm(`Delete “${p.name}”?`)) return
    setNotice(null)
    setActionError(null)
    try {
      await api(`/api/admin/packages/${p.id}`, { method: 'DELETE' })
      setNotice(`Deleted “${p.name}”.`)
      reload()
    } catch (err) {
      setActionError(err)
    }
  }

  return (
    <>
      <PageHeader
        title="Package plans"
        subtitle={
          <>
            The packages and prices shown on the website’s Packages page (optional: with none published, the page shows only the form). Requests
            visitors send from that page are in <Link to="/admin/inquiries?origin=packages">Requests → Package requests</Link>.
          </>
        }
        actions={
          <button type="button" className="adm-btn adm-btn--primary" onClick={() => (setNotice(null), setEditing('new'))}>
            + New package
          </button>
        }
      />
      <Notice>{notice}</Notice>
      <ErrorBanner error={actionError} />
      {editing === 'new' && <PackageForm onSaved={done} onCancel={() => setEditing(null)} />}
      {loading && <Spinner />}
      {error && <ErrorState error={error} onRetry={reload} />}
      {data?.length === 0 && editing !== 'new' && <p className="adm-muted">No packages yet.</p>}
      <div className="adm-stack">
        {data?.map((p) =>
          editing?.id === p.id ? (
            <PackageForm key={p.id} initial={p} onSaved={done} onCancel={() => setEditing(null)} />
          ) : (
            <article key={p.id} className="adm-card adm-row">
              <div>
                <strong>{p.name}</strong> {p.isPublished ? <StatusBadge tone="ok">Published</StatusBadge> : <StatusBadge>Hidden</StatusBadge>}
                <p className="adm-muted adm-small">{[p.priceNote, p.price != null && `${p.price} ${p.currency ?? ''}`].filter(Boolean).join(' ')}</p>
              </div>
              <div className="adm-form__actions">
                <button type="button" className="adm-btn adm-btn--small" onClick={() => (setNotice(null), setEditing(p))}>
                  Edit
                </button>
                <button type="button" className="adm-btn adm-btn--small adm-btn--danger-quiet" onClick={() => remove(p)}>
                  Delete
                </button>
              </div>
            </article>
          ),
        )}
      </div>
    </>
  )
}
