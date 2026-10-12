import { useState } from 'react'
import { Dialog } from '../ui/Dialog.jsx'

const albumStatus = {
  draft: { label: 'Draft', tone: 'neutral' },
  published: { label: 'Published', tone: 'ok' },
  disabled: { label: 'Disabled', tone: 'danger' },
}

export function StatusBadge({ tone = 'neutral', children }) {
  return <span className={`adm-badge adm-badge--${tone}`}>{children}</span>
}

export function AlbumStatusBadge({ status }) {
  const s = albumStatus[status] ?? { label: status, tone: 'neutral' }
  return <StatusBadge tone={s.tone}>{s.label}</StatusBadge>
}

export function PageHeader({ title, subtitle, actions, back }) {
  return (
    <header className="adm-page-head">
      <div>
        {back}
        <h1>{title}</h1>
        {subtitle && <p className="adm-muted">{subtitle}</p>}
      </div>
      {actions && <div className="adm-page-head__actions">{actions}</div>}
    </header>
  )
}

export function Pagination({ page, pageSize, total, onPage }) {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  if (pages <= 1) return null
  return (
    <nav className="adm-pagination" aria-label="Pagination">
      <button type="button" className="adm-btn adm-btn--quiet" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        ← Previous
      </button>
      <span className="adm-muted">
        Page {page} of {pages} · {total} total
      </span>
      <button type="button" className="adm-btn adm-btn--quiet" disabled={page >= pages} onClick={() => onPage(page + 1)}>
        Next →
      </button>
    </nav>
  )
}

/**
 * Shows login details exactly once after creating a client or resetting a password.
 * Passwords are never retrievable afterwards (only their hash is stored).
 */
export function CredentialsDialog({ credentials, onClose, title = 'Client login details' }) {
  const [copied, setCopied] = useState(false)
  if (!credentials) return null
  const loginUrl = `${window.location.origin}/client/login`
  const text = `Your Frans Studios gallery\n${loginUrl}\nUsername: ${credentials.username}\nPassword: ${credentials.password}`

  async function copy() {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }

  return (
    <Dialog
      open
      onClose={onClose}
      title={title}
      footer={
        <>
          <button type="button" className="adm-btn" onClick={copy}>
            {copied ? 'Copied ✓' : 'Copy message'}
          </button>
          <button type="button" className="adm-btn adm-btn--primary" onClick={onClose}>
            Done
          </button>
        </>
      }
    >
      <p className="adm-warning">Copy these details now — the password can’t be shown again. You can always reset it later.</p>
      <dl className="adm-credentials">
        <dt>Login page</dt>
        <dd>{loginUrl}</dd>
        <dt>Username</dt>
        <dd>
          <code>{credentials.username}</code>
        </dd>
        <dt>Password</dt>
        <dd>
          <code>{credentials.password}</code>
        </dd>
      </dl>
    </Dialog>
  )
}

export function ErrorBanner({ error }) {
  if (!error) return null
  const fields = Object.values(error.fieldErrors ?? {}).flat()
  return (
    <div className="form-error" role="alert">
      <p>{error.message}</p>
      {fields.length > 0 && (
        <ul>
          {fields.map((m) => (
            <li key={m}>{m}</li>
          ))}
        </ul>
      )}
    </div>
  )
}
