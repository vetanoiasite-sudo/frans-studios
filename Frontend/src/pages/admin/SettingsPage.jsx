import { useState } from 'react'
import { api } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { Field } from '../../components/ui/Field.jsx'
import { Spinner } from '../../components/ui/Spinner.jsx'
import { ErrorBanner, Notice, PageHeader, Pagination } from '../../components/admin/AdminUi.jsx'
import { formatDateTime } from '../../utils/format.js'

function ChangePassword() {
  const [form, setForm] = useState({ currentPassword: '', newPassword: '', confirm: '' })
  const [error, setError] = useState(null)
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  async function submit(e) {
    e.preventDefault()
    setError(null)
    setDone(false)
    if (form.newPassword !== form.confirm) {
      setError({ message: 'The new passwords don’t match.', fieldErrors: {} })
      return
    }
    setBusy(true)
    try {
      await api('/api/auth/change-password', { method: 'POST', body: { currentPassword: form.currentPassword, newPassword: form.newPassword } })
      setDone(true)
      setForm({ currentPassword: '', newPassword: '', confirm: '' })
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="adm-card adm-form" onSubmit={submit}>
      <h2>Change your password</h2>
      <p className="adm-muted adm-small">You’ll stay signed in here; all other devices are signed out.</p>
      <ErrorBanner error={error} />
      <Notice>{done && 'Password changed.'}</Notice>
      <Field label="Current password" type="password" autoComplete="current-password" required value={form.currentPassword} onChange={(e) => setForm({ ...form, currentPassword: e.target.value })} />
      <Field label="New password" type="password" autoComplete="new-password" minLength={10} required value={form.newPassword} onChange={(e) => setForm({ ...form, newPassword: e.target.value })} hint="At least 10 characters." />
      <Field label="Repeat new password" type="password" autoComplete="new-password" required value={form.confirm} onChange={(e) => setForm({ ...form, confirm: e.target.value })} />
      <div className="adm-form__actions">
        <button type="submit" className="adm-btn adm-btn--primary" disabled={busy}>
          {busy ? 'Saving…' : 'Change password'}
        </button>
      </div>
    </form>
  )
}

const parse = (json) => {
  try {
    return json ? JSON.parse(json) : {}
  } catch {
    return {}
  }
}

const quoted = (s) => (s ? `“${s}”` : '')

const albumStatus = { 0: 'draft', 1: 'published', 2: 'turned off' }

/** What a log entry means, in plain words: a short title and a detail line. */
function describe(a) {
  const d = parse(a.details)
  const subject = a.subject
  switch (a.action) {
    case 'login.success':
      return { title: 'Signed in', detail: a.clientName ? 'Client gallery' : 'Admin' }
    case 'login.failed':
      return { title: 'Failed sign-in', detail: d.username ? `Wrong password for “${d.username}”` : 'Wrong password' }
    case 'login.locked_out':
      return { title: 'Account locked', detail: 'Too many wrong passwords; locked for a while' }
    case 'logout':
      return { title: 'Signed out' }
    case 'password.changed':
      return { title: 'Changed their password' }
    case 'password.reset':
      return { title: 'Reset a client’s password', detail: subject }
    case 'client.created':
      return { title: 'Added a client', detail: [subject, d.username && `(${d.username})`].filter(Boolean).join(' ') }
    case 'client.updated':
      return { title: 'Edited a client', detail: subject }
    case 'client.disabled':
      return { title: 'Turned off a client’s access', detail: subject }
    case 'client.enabled':
      return { title: 'Turned on a client’s access', detail: subject }
    case 'client.deleted':
      return { title: 'Deleted a client', detail: d.FullName ?? subject }
    case 'album.created':
      return { title: 'Created an album', detail: quoted(d.Title ?? subject) }
    case 'album.updated': {
      const from = d.From ?? {}
      const to = d.To ?? {}
      if (from.Status !== to.Status && to.Status === 1) return { title: 'Published an album', detail: quoted(subject) }
      if (from.Status !== to.Status) return { title: `Set an album to ${albumStatus[to.Status] ?? 'another status'}`, detail: quoted(subject) }
      if (from.ClientId !== to.ClientId) return { title: 'Moved an album to another client', detail: quoted(subject) }
      return { title: 'Edited an album', detail: quoted(subject) }
    }
    case 'album.deleted':
      return { title: 'Deleted an album', detail: quoted(d.Title ?? subject) }
    case 'album.folder_assigned':
      return { title: 'Linked a Google Drive folder', detail: quoted(subject) }
    case 'album.folder_created':
      return { title: 'Created the album’s Drive folder', detail: quoted(subject) }
    case 'album.synced':
      return d.Success === false
        ? { title: 'Album sync failed', detail: quoted(subject) }
        : { title: 'Synced an album', detail: `${quoted(subject)} · ${d.FileCount ?? 0} files` }
    case 'album.move_requested':
      return { title: 'Started moving an album to the current Drive', detail: `${quoted(subject)} · from ${d.from}` }
    case 'album.moved':
      return { title: 'Moved an album to the current Drive', detail: `${quoted(subject)} · ${d.files ?? 0} files from ${d.from}` }
    case 'album.move_failed':
      return { title: 'Moving an album stopped', detail: `${quoted(subject)} · ${d.Error ?? ''}` }
    case 'file.uploaded':
      return { title: a.count > 1 ? `Uploaded ${a.count} files` : 'Uploaded a file', detail: subject && `to ${quoted(subject)}` }
    case 'file.downloaded':
      return { title: 'Downloaded a file', detail: [d.Name, subject && `from ${quoted(subject)}`].filter(Boolean).join(' ') }
    case 'section.downloaded':
      return {
        title: 'Downloaded a ZIP',
        detail: [`${d.Count ?? ''} files`, d.section && `(${d.section})`, subject && `from ${quoted(subject)}`].filter(Boolean).join(' '),
      }
    case 'drive.connected':
      return { title: 'Connected Google Drive', detail: d.AccountEmail }
    case 'drive.disconnected':
      return { title: 'Disconnected Google Drive' }
    case 'drive.folder_created':
      return { title: 'Created a Drive folder', detail: quoted(d.Name) }
    case 'drive.item_renamed':
      return { title: 'Renamed in Drive', detail: `${quoted(d.From)} → ${quoted(d.To)}` }
    case 'drive.items_moved':
      return { title: 'Moved items in Drive', detail: `${d.Done ?? ''} items` }
    case 'drive.items_trashed':
      return { title: 'Moved items to the Drive trash', detail: `${d.Done ?? ''} items` }
    case 'drive.upload_root_set':
      return { title: 'Chose the Drive folder for albums', detail: quoted(d.name) }
    default:
      return { title: a.action, detail: a.details }
  }
}

/**
 * One row for a run of uploads into the same album by the same person (an album upload logs one entry per file).
 * Entries come newest first.
 */
function groupUploads(items) {
  const rows = []
  for (const a of items) {
    const last = rows.at(-1)
    if (
      a.action === 'file.uploaded' &&
      last?.action === 'file.uploaded' &&
      last.entityId === a.entityId &&
      last.username === a.username &&
      new Date(last.oldest) - new Date(a.createdAt) < 30 * 60 * 1000
    ) {
      last.count += 1
      last.oldest = a.createdAt
    } else rows.push({ ...a, count: 1, oldest: a.createdAt })
  }
  return rows
}

const where = (ip) => (ip === '::1' || ip === '127.0.0.1' ? 'This computer' : ip)

function Activity() {
  const [page, setPage] = useState(1)
  const [category, setCategory] = useState('')
  const { data, loading } = useApi('/api/admin/audit', { page, category, pageSize: 50 })
  return (
    <section className="adm-card">
      <header className="adm-card__head">
        <h2>Activity log</h2>
        <select className="adm-input adm-input--small" aria-label="Filter activity" value={category} onChange={(e) => (setCategory(e.target.value), setPage(1))}>
          <option value="">All activity</option>
          <option value="signins">Sign-ins</option>
          <option value="downloads">Client downloads</option>
          <option value="uploads">Uploads</option>
          <option value="clients">Client changes</option>
          <option value="albums">Album changes</option>
          <option value="drive">Google Drive</option>
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
                  <th>What</th>
                  <th>Where</th>
                </tr>
              </thead>
              <tbody>
                {data.items.length === 0 && (
                  <tr>
                    <td colSpan={4} className="adm-muted">
                      Nothing here yet.
                    </td>
                  </tr>
                )}
                {groupUploads(data.items).map((a) => {
                  const { title, detail } = describe(a)
                  return (
                    <tr key={a.id}>
                      <td className="adm-nowrap">{formatDateTime(a.createdAt)}</td>
                      <td>
                        {a.clientName ?? a.username ?? '—'}
                        {a.clientName && <div className="adm-muted adm-small">Client</div>}
                      </td>
                      <td>
                        <strong>{title}</strong>
                        {detail && (
                          <div className="adm-muted adm-small adm-ellipsis" title={detail}>
                            {detail}
                          </div>
                        )}
                      </td>
                      <td className="adm-small adm-nowrap">{where(a.ipAddress)}</td>
                    </tr>
                  )
                })}
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
