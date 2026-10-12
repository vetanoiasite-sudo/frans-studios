import { useState } from 'react'
import { api } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { Field } from '../../components/ui/Field.jsx'
import { Spinner } from '../../components/ui/Spinner.jsx'
import { ErrorBanner, PageHeader, Pagination } from '../../components/admin/AdminUi.jsx'
import { formatDateTime } from '../../utils/format.js'

function ChangePassword() {
  const [form, setForm] = useState({ currentPassword: '', newPassword: '', confirm: '' })
  const [error, setError] = useState(null)
  const [done, setDone] = useState(false)

  async function submit(e) {
    e.preventDefault()
    setError(null)
    setDone(false)
    if (form.newPassword !== form.confirm) {
      setError({ message: 'The new passwords don’t match.', fieldErrors: {} })
      return
    }
    try {
      await api('/api/auth/change-password', { method: 'POST', body: { currentPassword: form.currentPassword, newPassword: form.newPassword } })
      setDone(true)
      setForm({ currentPassword: '', newPassword: '', confirm: '' })
    } catch (err) {
      setError(err)
    }
  }

  return (
    <form className="adm-card adm-form" onSubmit={submit}>
      <h2>Change your password</h2>
      <p className="adm-muted adm-small">You’ll stay signed in here; all other devices are signed out.</p>
      <ErrorBanner error={error} />
      {done && <p className="form-success">Password changed.</p>}
      <Field label="Current password" type="password" autoComplete="current-password" required value={form.currentPassword} onChange={(e) => setForm({ ...form, currentPassword: e.target.value })} />
      <Field label="New password" type="password" autoComplete="new-password" minLength={10} required value={form.newPassword} onChange={(e) => setForm({ ...form, newPassword: e.target.value })} hint="At least 10 characters." />
      <Field label="Repeat new password" type="password" autoComplete="new-password" required value={form.confirm} onChange={(e) => setForm({ ...form, confirm: e.target.value })} />
      <div className="adm-form__actions">
        <button type="submit" className="adm-btn adm-btn--primary">
          Change password
        </button>
      </div>
    </form>
  )
}

function Activity() {
  const [page, setPage] = useState(1)
  const [action, setAction] = useState('')
  const { data, loading } = useApi('/api/admin/audit', { page, action, pageSize: 50 })
  return (
    <section className="adm-card">
      <header className="adm-card__head">
        <h2>Activity log</h2>
        <select className="adm-input adm-input--small" aria-label="Filter activity" value={action} onChange={(e) => (setAction(e.target.value), setPage(1))}>
          <option value="">All activity</option>
          <option value="login">Sign-ins</option>
          <option value="file.">Downloads</option>
          <option value="client.">Client changes</option>
          <option value="album.">Album changes</option>
          <option value="drive.">Google Drive</option>
        </select>
      </header>
      {loading && !data && <Spinner />}
      {data && (
        <>
          <div className="adm-table-wrap">
            <table className="adm-table adm-table--compact">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Who</th>
                  <th>Action</th>
                  <th>Details</th>
                  <th>IP</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((a) => (
                  <tr key={a.id}>
                    <td className="adm-nowrap">{formatDateTime(a.createdAt)}</td>
                    <td>{a.username ?? '—'}</td>
                    <td>
                      <code>{a.action}</code>
                    </td>
                    <td className="adm-small adm-ellipsis" title={a.details ?? ''}>
                      {[a.entityType, a.entityId].filter(Boolean).join(' #')} {a.details}
                    </td>
                    <td className="adm-small">{a.ipAddress}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={data.page} pageSize={data.pageSize} total={data.total} onPage={setPage} />
        </>
      )}
    </section>
  )
}

export default function SettingsPage() {
  return (
    <>
      <PageHeader title="Settings & activity" />
      <ChangePassword />
      <Activity />
    </>
  )
}
