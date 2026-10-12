import { useEffect, useRef, useState } from 'react'
import { UploadQueue, acceptable, albumTarget, filesFromDrop, sectionFor } from '../../utils/driveUpload.js'
import { formatBytes } from '../../utils/format.js'

const statusLabel = { queued: 'Waiting', uploading: 'Uploading', paused: 'Paused', error: 'Failed', done: 'Uploaded' }

/**
 * "Upload to Google Drive" panel on an album: drop files or a whole folder, pick the section (or let it
 * follow the folder's own Photos / Videos sub-folders), and watch each file go into the album's Drive
 * folder. Uploads keep going while you work elsewhere on this page, survive dropped connections, and can be
 * paused and resumed. When a batch finishes the album is synced so the files show up straight away.
 *
 * @param {{ album: { id: number, sections: { name: string }[] }, onUploaded: () => void }} props
 */
export function DriveUploader({ album, onUploaded }) {
  const [, setTick] = useState(0)
  const [choice, setChoice] = useState('auto')
  const [custom, setCustom] = useState('')
  const [dragging, setDragging] = useState(false)
  const filesInput = useRef(null)
  const folderInput = useRef(null)
  const onUploadedRef = useRef(onUploaded)
  onUploadedRef.current = onUploaded

  // One queue per album, kept across re-renders. Progress events are batched to one render per frame.
  const queueRef = useRef(null)
  if (!queueRef.current || queueRef.current.target.id !== albumTarget(album.id).id) {
    let frame = 0
    queueRef.current = new UploadQueue(albumTarget(album.id), {
      onChange: () => {
        if (!frame) frame = requestAnimationFrame(() => {
          frame = 0
          setTick((t) => t + 1)
        })
      },
      onFinished: () => onUploadedRef.current?.(),
    })
  }
  const queue = queueRef.current

  // Leaving the page would stop the uploads: ask first.
  useEffect(() => {
    const warn = (e) => {
      if (!queue.busy) return
      e.preventDefault()
      e.returnValue = ''
    }
    window.addEventListener('beforeunload', warn)
    return () => {
      window.removeEventListener('beforeunload', warn)
      queue.pauseAll()
    }
  }, [queue])

  const section = choice === 'other' ? custom.trim() : choice
  const sectionInvalid = choice === 'other' && (!section || /[/\\]/.test(section) || section.length > 100)

  function addFiles(list) {
    if (sectionInvalid) return
    const entries = list.filter(({ file }) => acceptable(file)).map(({ file, path }) => ({ file, dest: sectionFor(file, path, section) }))
    if (entries.length) queue.add(entries)
  }

  function fromInput(e) {
    addFiles([...e.target.files].map((file) => ({ file, path: file.webkitRelativePath || file.name })))
    e.target.value = ''
  }

  async function onDrop(e) {
    e.preventDefault()
    setDragging(false)
    addFiles(await filesFromDrop(e.dataTransfer))
  }

  const { size, sent, counts, total } = queue.totals()
  const pct = size ? Math.floor((sent / size) * 100) : 0
  const sections = [...new Set(['Photos', 'Videos', ...album.sections.map((s) => s.name).filter(Boolean)])]
  const visible = queue.items.filter((i) => i.status !== 'done')

  return (
    <section className="adm-card adm-uploader">
      <header className="adm-card__head">
        <h2>Upload to Google Drive</h2>
        <span className="adm-muted adm-small">Files go straight into this album’s Drive folder.</span>
      </header>

      <div className="adm-uploader__section">
        <label className="field__label" htmlFor="upload-section">
          Put files in
        </label>
        <select id="upload-section" className="adm-input" value={choice} onChange={(e) => setChoice(e.target.value)}>
          <option value="auto">Automatic: keep the folder’s Photos / Videos sub-folders, otherwise by file type</option>
          {sections.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
          <option value="other">Another section…</option>
        </select>
        {choice === 'other' && (
          <input className="adm-input" placeholder="Section name, e.g. Highlights" value={custom} onChange={(e) => setCustom(e.target.value)} maxLength={100} aria-label="New section name" />
        )}
        {sectionInvalid && custom && <p className="field__error">Use a single folder name without slashes.</p>}
      </div>

      <div
        className={`adm-dropzone adm-uploader__drop${dragging ? ' is-dragging' : ''}`}
        onDragOver={(e) => {
          e.preventDefault()
          setDragging(true)
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
      >
        <p>
          <strong>Drag photos, videos or a whole folder here</strong>
        </p>
        <p className="adm-muted adm-small">Large files are fine: they’re sent in pieces and continue after a dropped connection.</p>
        <div className="adm-form__actions">
          <button type="button" className="adm-btn" onClick={() => filesInput.current?.click()} disabled={sectionInvalid}>
            Choose files
          </button>
          <button type="button" className="adm-btn" onClick={() => folderInput.current?.click()} disabled={sectionInvalid}>
            Choose a folder
          </button>
        </div>
        <input ref={filesInput} type="file" multiple hidden onChange={fromInput} />
        <input ref={folderInput} type="file" multiple hidden webkitdirectory="" onChange={fromInput} />
      </div>

      {total > 0 && (
        <div className="adm-uploader__summary" aria-live="polite">
          <div className="adm-progress" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label="Overall upload progress">
            <span style={{ width: `${pct}%` }} />
          </div>
          <p className="adm-small">
            <strong>{counts.done}</strong> of {total} files · {formatBytes(sent)} of {formatBytes(size)} ({pct}%)
            {counts.error > 0 && <span className="adm-text-danger"> · {counts.error} failed</span>}
            {counts.paused > 0 && <span> · {counts.paused} paused</span>}
          </p>
          <div className="adm-form__actions">
            {queue.busy ? (
              <button type="button" className="adm-btn" onClick={() => queue.pauseAll()}>
                Pause all
              </button>
            ) : (
              (counts.paused > 0 || counts.error > 0) && (
                <button type="button" className="adm-btn adm-btn--primary" onClick={() => queue.resumeAll()}>
                  {counts.error > 0 ? 'Retry failed & resume' : 'Resume all'}
                </button>
              )
            )}
            {counts.done > 0 && (
              <button type="button" className="adm-link-btn adm-small" onClick={() => queue.clearDone()}>
                Clear uploaded
              </button>
            )}
          </div>
        </div>
      )}

      {visible.length > 0 && (
        <ul className="adm-uploads" role="list">
          {visible.slice(0, 200).map((item) => {
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
                  <button type="button" className="adm-link-btn adm-small adm-text-danger" onClick={() => queue.remove(item)} aria-label={`Remove ${item.name} from the list`}>
                    Remove
                  </button>
                </div>
              </li>
            )
          })}
          {visible.length > 200 && <li className="adm-muted adm-small">…and {visible.length - 200} more waiting.</li>}
        </ul>
      )}

      <p className="adm-muted adm-small">
        Keep this page open until uploads finish. If it closes, choose the same files again: they continue where they stopped. Files the studio adds to the
        album’s Drive folder by hand show up too after “Sync now”.
      </p>
    </section>
  )
}
