import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { api } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { Field } from '../../components/ui/Field.jsx'
import { ConfirmDialog } from '../../components/ui/Dialog.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { AlbumStatusBadge, CredentialsDialog, ErrorBanner, PageHeader, StatusBadge } from '../../components/admin/AdminUi.jsx'
import { formatBytes, formatDate, formatDateTime } from '../../utils/format.js'

export default function ClientDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const { data: client, error, loading, reload } = useApi(`/api/admin/clients/${id}`)
  const [form, setForm] = useState(null)
  const [saveError, setSaveError] = useState(null)
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false)
  const [confirm, setConfirm] = useState(null) // 'disable' | 'enable' | 'reset' | 'delete'
  const [credentials, setCredentials] = useState(null)

  useEffect(() => {
    if (client) setForm({ fullName: client.fullName, username: client.username, email: client.email ?? '', phone: client.phone ?? '', notes: client.notes ?? '' })
  }, [client])

  if (loading) return <Spinner />
  if (error) return <ErrorState error={error} onRetry={reload} />
  if (!client || !form) return null

  const set = (name) => (e) => {
    setSaved(false)
    setForm((f) => ({ ...f, [name]: e.target.value }))
  }

  async function save(e) {
    e.preventDefault()
    setBusy(true)
    setSaveError(null)
    try {
      await api(`/api/admin/clients/${id}`, { method: 'PUT', body: { ...form, email: form.email || null } })
      setSaved(true)
      reload()
    } catch (err) {
      setSaveError(err)
    } finally {
      setBusy(false)
    }
  }

  async function runConfirmed() {
    setBusy(true)
    try {
      if (confirm === 'delete') {
        await api(`/api/admin/clients/${id}`, { method: 'DELETE' })
        navigate('/admin/clients', { replace: true })
        return
      }
      if (confirm === 'reset') {
        setCredentials(await api(`/api/admin/clients/${id}/reset-password`, { method: 'POST', body: {} }))
      } else {
        await api(`/api/admin/clients/${id}/${confirm}`, { method: 'POST' })
      }
      reload()
    } catch (err) {
      setSaveError(err)
    } finally {
      setBusy(false)
      setConfirm(null)
    }
  }

  const fe = (name) => saveError?.fieldErrors?.[name]
  const confirmText = {
    disable: { title: 'Disable client?', message: `${client.fullName} will be signed out immediately and can’t sign in until re-enabled.`, label: 'Disable', danger: true },
    enable: { title: 'Enable client?', message: `${client.fullName} will be able to sign in again.`, label: 'Enable' },
    reset: { title: 'Reset password?', message: 'A new password is generated and the client is signed out everywhere. You’ll need to send them the new password.', label: 'Reset password', danger: true },
    delete: { title: 'Delete client?', message: 'The client, their login and their albums are removed from the website. Files in Google Drive are NOT touched.', label: 'Delete client', danger: true },
  }[confirm ?? 'enable']

  return (
    <>
      <PageHeader
        title={client.fullName}
        subtitle={
          <>
            {client.isActive ? <StatusBadge tone="ok">Active</StatusBadge> : <StatusBadge tone="danger">Disabled</StatusBadge>} · Last login{' '}
            {client.lastLoginAt ? formatDateTime(client.lastLoginAt) : 'never'}
          </>
        }
        back={<Link to="/admin/clients" className="adm-back">← Clients</Link>}
        actions={
          <>
            <button type="button" className="adm-btn" onClick={() => setConfirm('reset')}>
              Reset password
            </button>
            <button type="button" className="adm-btn" onClick={() => setConfirm(client.isActive ? 'disable' : 'enable')}>
              {client.isActive ? 'Disable' : 'Enable'}
            </button>
          </>
        }
      />

      <div className="adm-columns">
        <form className="adm-card adm-form" onSubmit={save}>
          <h2>Details</h2>
          <ErrorBanner error={saveError} />
          {saved && <p className="form-success">Saved.</p>}
          <Field label="Full name" required value={form.fullName} onChange={set('fullName')} error={fe('fullName')} />
          <Field label="Username" required autoCapitalize="none" value={form.username} onChange={set('username')} error={fe('username')} />
          <div className="adm-form__row">
            <Field label="Email" type="email" value={form.email} onChange={set('email')} error={fe('email')} />
            <Field label="Phone" type="tel" value={form.phone} onChange={set('phone')} />
          </div>
          <Field label="Private notes" as="textarea" rows={3} value={form.notes} onChange={set('notes')} />
          <div className="adm-form__actions">
            <button type="submit" className="adm-btn adm-btn--primary" disabled={busy}>
              Save changes
            </button>
            <button type="button" className="adm-btn adm-btn--danger-quiet" onClick={() => setConfirm('delete')}>
              Delete client
            </button>
          </div>
        </form>

        <div className="adm-stack">
          <section className="adm-card">
            <header className="adm-card__head">
              <h2>Albums</h2>
              <Link to={`/admin/albums/new?clientId=${client.id}`} className="adm-btn adm-btn--small">
                + New album
              </Link>
            </header>
            {client.albums.length === 0 ? (
              <p className="adm-muted">No albums yet.</p>
            ) : (
              <ul className="adm-list">
                {client.albums.map((a) => (
                  <li key={a.id}>
                    <Link to={`/admin/albums/${a.id}`} className="adm-row-link">
                      {a.title}
                    </Link>
                    <span className="adm-muted adm-small">
                      {[a.eventDate && formatDate(a.eventDate), `${a.fileCount} files`, formatBytes(a.totalBytes)].filter(Boolean).join(' · ')}
                    </span>
                    <AlbumStatusBadge status={a.status} />
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="adm-card">
            <h2>Recent activity</h2>
            {client.recentActivity.length === 0 ? (
              <p className="adm-muted">No activity yet.</p>
            ) : (
              <ul className="adm-activity">
                {client.recentActivity.map((a, i) => (
                  <li key={i}>
                    <code>{a.action}</code>
                    <span className="adm-muted adm-small">{formatDateTime(a.createdAt)}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      </div>

      <ConfirmDialog
        open={confirm !== null}
        title={confirmText.title}
        message={confirmText.message}
        confirmLabel={confirmText.label}
        danger={confirmText.danger}
        busy={busy}
        onConfirm={runConfirmed}
        onCancel={() => setConfirm(null)}
      />
      <CredentialsDialog credentials={credentials} onClose={() => setCredentials(null)} title="New password" />
    </>
  )
}
