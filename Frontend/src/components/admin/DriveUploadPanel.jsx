import { useState } from 'react'
import { formatBytes } from '../../utils/format.js'

const statusLabel = { queued: 'Waiting', uploading: 'Uploading', paused: 'Paused', error: 'Failed', done: 'Uploaded' }

/**
 * Floating upload progress panel (bottom corner, like Google Drive's) for an UploadQueue: overall progress,
 * pause / resume / retry, and the files still in flight. Collapsible; hidden when the queue is empty.
 *
 * @param {{ queue: import('../../utils/driveUpload.js').UploadQueue }} props
 */
export function DriveUploadPanel({ queue }) {
  const [collapsed, setCollapsed] = useState(false)
  const { size, sent, counts, total } = queue.totals()
  if (!total) return null

  const pct = size ? Math.floor((sent / size) * 100) : 0
  const visible = queue.items.filter((i) => i.status !== 'done')
  const title = queue.busy ? `Uploading ${total - counts.done} of ${total}` : counts.error ? `${counts.error} upload(s) failed` : `${counts.done} uploaded`

  return (
    <section className="adm-upload-panel" aria-label="Uploads">
      <header className="adm-upload-panel__head">
        <strong>{title}</strong>
        <span className="adm-muted adm-small">
          {formatBytes(sent)} / {formatBytes(size)} · {pct}%
        </span>
        <button type="button" className="adm-link-btn adm-small" onClick={() => setCollapsed((c) => !c)} aria-expanded={!collapsed}>
          {collapsed ? 'Show' : 'Hide'}
        </button>
        {!queue.busy && (
          <button type="button" className="adm-link-btn adm-small" onClick={() => queue.clearDone()}>
            Close
          </button>
        )}
      </header>
      <div className="adm-progress" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label="Overall upload progress">
        <span style={{ width: `${pct}%` }} />
      </div>

      {!collapsed && (
        <>
          <div className="adm-form__actions">
            {queue.busy ? (
              <button type="button" className="adm-btn adm-btn--small" onClick={() => queue.pauseAll()}>
                Pause all
              </button>
            ) : (
              (counts.paused > 0 || counts.error > 0) && (
                <button type="button" className="adm-btn adm-btn--small adm-btn--primary" onClick={() => queue.resumeAll()}>
                  {counts.error > 0 ? 'Retry failed & resume' : 'Resume all'}
                </button>
              )
            )}
          </div>
          {visible.length > 0 && (
            <ul className="adm-uploads adm-upload-panel__list" role="list">
              {visible.slice(0, 100).map((item) => {
                const p = item.size ? Math.floor((item.sent / item.size) * 100) : 0
                return (
                  <li key={item.id} className={`adm-uploads__row is-${item.status}`}>
                    <div className="adm-uploads__name">
                      <span className="adm-ellipsis" title={item.name}>
                        {item.name}
                      </span>
                      <span className="adm-muted adm-small">
                        {item.destLabel} · {formatBytes(item.size)} · {statusLabel[item.status]}
                        {item.status === 'uploading' || item.status === 'paused' ? ` ${p}%` : ''}
                      </span>
                      {item.error && <span className="adm-text-danger adm-small">{item.error}</span>}
                    </div>
                    <div className="adm-progress adm-progress--small" aria-hidden="true">
                      <span style={{ width: `${p}%` }} />
                    </div>
                    <div className="adm-uploads__actions">
                      {(item.status === 'uploading' || item.status === 'queued') && (
                        <button type="button" className="adm-link-btn adm-small" onClick={() => queue.pause(item)}>
                          Pause
                        </button>
                      )}
                      {(item.status === 'paused' || item.status === 'error') && (
                        <button type="button" className="adm-link-btn adm-small" onClick={() => queue.resume(item)}>
                          {item.status === 'error' ? 'Retry' : 'Resume'}
                        </button>
                      )}
                      <button type="button" className="adm-link-btn adm-small adm-text-danger" onClick={() => queue.remove(item)} aria-label={`Remove ${item.name}`}>
                        Remove
                      </button>
                    </div>
                  </li>
                )
              })}
              {visible.length > 100 && <li className="adm-muted adm-small">…and {visible.length - 100} more waiting.</li>}
            </ul>
          )}
        </>
      )}
    </section>
  )
}
