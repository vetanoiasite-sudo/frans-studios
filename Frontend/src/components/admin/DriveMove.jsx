import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { api } from '../../services/api.js'
import { ErrorBanner, StatusBadge } from './AdminUi.jsx'

/**
 * Albums whose Drive folder is in a previously connected Google account's Drive ("driveMove" on an album) keep
 * working for clients, and can be moved to the connected Drive: Google copies the files across, then the album
 * switches over. These pieces show that state and start the move.
 */

const isMoving = (move) => move?.state === 'queued' || move?.state === 'running'

/** Calls `reload` every few seconds while `active`, to follow a move's progress. */
export function usePollWhile(active, reload, ms = 2500) {
  useEffect(() => {
    if (!active) return
    const timer = setInterval(reload, ms)
    return () => clearInterval(timer)
  }, [active, reload, ms])
}

function Progress({ move }) {
  const pct = move.total ? Math.round((move.done / move.total) * 100) : 0
  return (
    <>
      <div className="adm-progress" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label="Move progress">
        <span style={{ width: `${pct}%` }} />
      </div>
      <p className="adm-small" aria-live="polite">
        {move.state === 'queued' || !move.total ? 'Starting…' : `${move.done} of ${move.total} files copied`}
      </p>
    </>
  )
}

/** Album page: where the files are, and the move to the connected Drive. */
export function DriveMoveNotice({ album, connectedEmail, onChanged }) {
  const move = album.driveMove
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  usePollWhile(isMoving(move), onChanged)
  if (!move) return null

  async function start() {
    setBusy(true)
    setError(null)
    try {
      await api(`/api/admin/albums/${album.id}/move-drive`, { method: 'POST' })
      onChanged()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  if (!move.available)
    return (
      <section className="adm-callout adm-callout--danger adm-drive-move">
        <h2>⚠ The client can’t open this album</h2>
        <p>
          Its files are in <strong>{move.accountEmail}</strong>’s Drive, and Google no longer lets the website reach that account (its access expired or
          was removed).
        </p>
        <p className="adm-small">
          To fix it: in <Link to="/admin/google-drive">Drive settings</Link>, press <em>Reconnect / switch account</em> and sign in as{' '}
          <strong>{move.accountEmail}</strong>. The album works again straight away. Then reconnect <strong>{connectedEmail ?? 'the current account'}</strong>{' '}
          and move it. Or choose a folder for it in the current Drive below.
        </p>
      </section>
    )

  if (isMoving(move))
    return (
      <section className="adm-callout adm-callout--warn adm-drive-move" aria-label="Moving to the current Google Drive">
        <h2>Moving to the current Google Drive…</h2>
        <p className="adm-small">
          Google is copying the files from <strong>{move.accountEmail}</strong> to <strong>{connectedEmail ?? 'the connected account'}</strong>. The client
          keeps seeing the album meanwhile. You can leave this page.
        </p>
        <Progress move={move} />
      </section>
    )

  return (
    <section className={`adm-callout ${move.state === 'failed' ? 'adm-callout--danger' : 'adm-callout--warn'} adm-drive-move`}>
      <h2>⚠ Still in the old Google Drive</h2>
      <p>
        This album’s files are in <strong>{move.accountEmail}</strong>’s Drive, not the connected one ({connectedEmail ?? 'none'}). The client can still see
        them. Move them so everything is in one Drive.
      </p>
      {move.state === 'failed' && <p className="form-error">The last move stopped: {move.error}</p>}
      <ErrorBanner error={error} />
      <div className="adm-form__actions">
        <button type="button" className="adm-btn adm-btn--primary" onClick={start} disabled={busy}>
          {busy ? 'Starting…' : move.state === 'failed' ? 'Try again' : 'Move to the current Drive'}
        </button>
      </div>
      <p className="adm-muted adm-small">
        Google copies the files into <em>My Drive / Frans Studios Albums</em> (big albums take a while). The originals stay in the old Drive until you
        delete them there.
      </p>
    </section>
  )
}

/** Albums list: a short label for an album that is in another account's Drive. */
export function DriveMoveBadge({ move }) {
  if (!move) return null
  if (!move.available)
    return (
      <span title={`In ${move.accountEmail}’s Drive, which the website can no longer reach`}>
        <StatusBadge tone="danger">Old Drive · no access</StatusBadge>
      </span>
    )
  if (isMoving(move)) return <StatusBadge tone="warn">Moving {move.total ? `${move.done}/${move.total}` : '…'}</StatusBadge>
  if (move.state === 'failed') return <StatusBadge tone="danger">Move failed</StatusBadge>
  return (
    <span title={`In ${move.accountEmail}’s Drive`}>
      <StatusBadge tone="warn">Old Drive</StatusBadge>
    </span>
  )
}

/**
 * Albums and Drive settings pages: one line per previously connected account that still holds albums,
 * with "Move all". `status` is /api/admin/google-drive/status.
 */
export function OtherDrivesBanner({ status, onChanged }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  const others = status?.otherAccounts ?? []
  usePollWhile(others.some((o) => o.moving > 0), onChanged)
  if (!status?.connected || others.length === 0) return null

  async function moveAll() {
    setBusy(true)
    setError(null)
    try {
      await api('/api/admin/albums/move-drive', { method: 'POST' })
      onChanged()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  const waiting = others.filter((o) => o.available).reduce((n, o) => n + o.albums - o.moving, 0)
  const lost = others.some((o) => !o.available)
  return (
    <section className={`adm-callout ${lost ? 'adm-callout--danger' : 'adm-callout--warn'} adm-drive-move`}>
      <h2>⚠ Albums still in an old Google Drive</h2>
      <ul className="adm-drive-move__list">
        {others.map((o) => (
          <li key={o.email}>
            <strong>
              {o.albums} {o.albums === 1 ? 'album' : 'albums'}
            </strong>{' '}
            in <strong>{o.email}</strong>’s Drive
            {o.moving > 0 && <> · moving {o.moving} now…</>}
            {o.failed > 0 && <span className="adm-text-danger"> · {o.failed} stopped with an error (open the album to try again)</span>}
            {!o.available && (
              <div className="adm-text-danger adm-small">
                Google no longer lets the website reach this account (its access expired or was removed), so <strong>clients can’t open these albums</strong>.
                Press <em>Reconnect / switch account</em> in Drive settings and sign in as {o.email}: the albums work again straight away. Then reconnect the
                current account and move them.
              </div>
            )}
          </li>
        ))}
      </ul>
      {waiting > 0 && (
        <p className="adm-small">
          {lost ? 'The albums of the accounts that still work are fine: clients can see them.' : 'Clients can still see these albums.'} Moving copies their
          files into the connected Drive ({status.accountEmail}); the originals stay in the old one.
        </p>
      )}
      <ErrorBanner error={error} />
      {waiting > 0 && (
        <div className="adm-form__actions">
          <button type="button" className="adm-btn adm-btn--primary" onClick={moveAll} disabled={busy}>
            {busy ? 'Starting…' : `Move ${waiting === 1 ? 'it' : `all ${waiting}`} to the current Drive`}
          </button>
        </div>
      )}
    </section>
  )
}
