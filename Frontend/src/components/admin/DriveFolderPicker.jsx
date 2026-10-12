import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { useApi } from '../../hooks/useApi.js'
import { Dialog } from '../ui/Dialog.jsx'
import { Spinner } from '../ui/Spinner.jsx'
import { formatDate } from '../../utils/format.js'

/** Opens a folder in Google Drive (the studio is signed in there). */
export const driveFolderUrl = (id) =>
  id === 'root' ? 'https://drive.google.com/drive/my-drive' : `https://drive.google.com/drive/folders/${encodeURIComponent(id)}`

/**
 * Drive-like folder browser. All Drive calls go through the API with the studio's server-side
 * token — the browser never sees a Google credential. Also accepts a pasted Drive folder link.
 * onSelect(folder, label): folder is the id (or pasted link); label is its display path, e.g.
 * "My Drive / Clients / Sara & Ahmed" (just the name for search results, null for a pasted link).
 */
export function DriveFolderPicker({ open, onClose, onSelect, title = 'Choose a Google Drive folder', allowRoot = false, hidePaste = false }) {
  const [trail, setTrail] = useState([{ id: 'root', name: 'My Drive' }])
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [pasted, setPasted] = useState('')
  const current = trail[trail.length - 1]
  const pathOf = (extra) => [...trail.map((f) => f.name), ...(extra ? [extra] : [])].join(' / ')

  useEffect(() => {
    const id = setTimeout(() => setQuery(search.trim()), 350)
    return () => clearTimeout(id)
  }, [search])

  const { data: folders, error, loading } = useApi(open ? '/api/admin/google-drive/folders' : null, query ? { q: query } : { parentId: current.id })

  function enter(folder) {
    setSearch('')
    setQuery('')
    setTrail((t) => [...t, { id: folder.id, name: folder.name }])
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={title}
      wide
      footer={
        <>
          <button type="button" className="adm-btn" onClick={onClose}>
            Cancel
          </button>
          {!query && (allowRoot || current.id !== 'root') && (
            <button type="button" className="adm-btn adm-btn--primary" onClick={() => onSelect(current.id, pathOf())}>
              Use “{current.name}”
            </button>
          )}
        </>
      }
    >
      <div className="adm-picker">
        <input type="search" className="adm-input" placeholder="Search all folders by name…" aria-label="Search folders" value={search} onChange={(e) => setSearch(e.target.value)} />

        {!query && (
          <nav className="adm-picker__trail" aria-label="Folder path">
            {trail.map((f, i) => (
              <span key={f.id}>
                {i > 0 && <span aria-hidden="true"> / </span>}
                <button type="button" className="adm-link-btn" disabled={i === trail.length - 1} onClick={() => setTrail((t) => t.slice(0, i + 1))}>
                  {f.name}
                </button>
              </span>
            ))}
          </nav>
        )}

        {loading && <Spinner />}
        {error && (
          <p className="form-error">
            {error.status === 503 ? (
              <>
                {error.message} <Link to="/admin/google-drive">Open Google Drive settings</Link>
              </>
            ) : (
              error.message
            )}
          </p>
        )}
        {folders && folders.length === 0 && <p className="adm-muted">{query ? 'No folders match.' : 'This folder has no sub-folders.'}</p>}
        {folders && folders.length > 0 && (
          <ul className="adm-picker__list" role="list">
            {folders.map((f) => (
              <li key={f.id} className="adm-picker__item">
                <button type="button" className="adm-picker__open" onClick={() => enter(f)}>
                  <span aria-hidden="true">📁</span> {f.name}
                  {f.modifiedTime && <span className="adm-muted adm-small">{formatDate(f.modifiedTime)}</span>}
                </button>
                <button type="button" className="adm-btn adm-btn--small" onClick={() => onSelect(f.id, query ? f.name : pathOf(f.name))}>
                  Select
                </button>
              </li>
            ))}
          </ul>
        )}

        {!hidePaste && (
        <details className="adm-picker__paste">
          <summary>Paste a Drive folder link instead</summary>
          <div className="adm-picker__paste-row">
            <input className="adm-input" placeholder="https://drive.google.com/drive/folders/…" value={pasted} onChange={(e) => setPasted(e.target.value)} />
            <button type="button" className="adm-btn" disabled={!pasted.trim()} onClick={() => onSelect(pasted.trim(), null)}>
              Use link
            </button>
          </div>
        </details>
        )}
      </div>
    </Dialog>
  )
}
