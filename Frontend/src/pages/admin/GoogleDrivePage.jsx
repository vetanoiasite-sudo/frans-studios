import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { api } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { ConfirmDialog } from '../../components/ui/Dialog.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { PageHeader, StatusBadge } from '../../components/admin/AdminUi.jsx'
import { formatDateTime } from '../../utils/format.js'
import { OtherDrivesBanner } from '../../components/admin/DriveMove.jsx'

const errors = {
  consent_denied: 'Google access was not granted.',
  invalid_state: 'The connection attempt expired or was invalid. Please try again.',
  scope_missing: 'Drive access was not granted. On the Google screen, make sure the Google Drive permission is ticked.',
  no_refresh_token: 'Google did not return long-term access. Remove “Frans Studios” at myaccount.google.com/permissions, then connect again.',
  token_exchange_failed: 'Google rejected the connection. Check the OAuth client settings on the server.',
}

export default function GoogleDrivePage() {
  const [params] = useSearchParams()
  const { data, error, loading, reload } = useApi('/api/admin/google-drive/status')
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)

  if (loading) return <Spinner />
  if (error) return <ErrorState error={error} onRetry={reload} />

  async function disconnect() {
    setBusy(true)
    try {
      await api('/api/admin/google-drive/disconnect', { method: 'POST' })
      reload()
    } finally {
      setBusy(false)
      setConfirm(false)
    }
  }

  const errorCode = params.get('error')

  return (
    <>
      <PageHeader
        title="Drive settings"
        subtitle={
          <>
            Connect the studio’s Google account. To browse and upload files, use <Link to="/admin/drive">Drive</Link>.
          </>
        }
      />

      {params.get('connected') && <p className="form-success">Google Drive connected.</p>}
      {errorCode && <p className="form-error">{errors[errorCode] ?? 'The connection failed. Please try again.'}</p>}

      <section className="adm-card">
        <h2>Connection</h2>
        {!data.configured ? (
          <div className="adm-callout adm-callout--warn">
            Google OAuth isn’t configured on the server yet. Set <code>GoogleDrive__ClientId</code> and <code>GoogleDrive__ClientSecret</code> (see docs/DEPLOYMENT.md), then reload this page.
          </div>
        ) : data.connected ? (
          <>
            <p>
              <StatusBadge tone="ok">Connected</StatusBadge> as <strong>{data.accountEmail}</strong> since {formatDateTime(data.connectedAt)}
            </p>
            {!data.canUpload && (
              <div className="adm-callout adm-callout--warn">
                This connection is <strong>read-only</strong>, so albums can’t be uploaded from the website yet. Press <strong>Reconnect</strong> and allow
                Google Drive access to turn uploads on. Existing albums keep working.
              </div>
            )}
            <div className="adm-form__actions">
              <a href="/api/admin/google-drive/connect" className="adm-btn">
                Reconnect / switch account
              </a>
              <button type="button" className="adm-btn adm-btn--danger-quiet" onClick={() => setConfirm(true)}>
                Disconnect
              </button>
            </div>
          </>
        ) : (
          <>
            <p>
              <StatusBadge tone="danger">Not connected</StatusBadge>
            </p>
            <p className="adm-muted">You’ll be sent to Google to sign in with the studio’s account and allow access to its Drive.</p>
            <div className="adm-form__actions">
              <a href="/api/admin/google-drive/connect" className="adm-btn adm-btn--primary">
                Connect Google Drive
              </a>
            </div>
          </>
        )}
      </section>

      <OtherDrivesBanner status={data} onChanged={reload} />

      <section className="adm-card adm-prose">
        <h2>How it works</h2>
        <ul>
          <li>
            Two ways to fill an album, and both end up in the same Drive folder: <strong>upload from the album page</strong> here, or put the files in the
            album’s folder in Drive yourself and press “Sync now”.
          </li>
          <li>The website only creates folders and adds files. It never deletes or changes anything in your Drive.</li>
          <li>
            Switching to another Google account doesn’t break existing albums: they keep working from the old account’s Drive, and “Move to the current Drive”
            copies them across (the old folder is shared with the new account only while copying). The originals are never deleted.
          </li>
          <li>Google credentials are stored encrypted on the server and are never sent to browsers.</li>
          <li>Clients never see Drive links. Files are streamed to them through the website only after their login is checked.</li>
          <li>
            New albums get their own folder in <code>My Drive / Frans Studios Albums</code>, with Photos and Videos inside. The website creates
            that folder by itself the first time. Each sub-folder becomes a tab in the client’s gallery.
          </li>
        </ul>
        <p className="adm-muted adm-small">OAuth redirect URI to register in Google Cloud: <code>{data.redirectUri}</code></p>
      </section>

      <ConfirmDialog
        open={confirm}
        title="Disconnect Google Drive?"
        message="Client galleries will stop showing files until Drive is connected again. Nothing in your Drive is deleted."
        confirmLabel="Disconnect"
        danger
        busy={busy}
        onConfirm={disconnect}
        onCancel={() => setConfirm(false)}
      />
    </>
  )
}
