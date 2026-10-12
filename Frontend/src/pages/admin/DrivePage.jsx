import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { api } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { Dialog, ConfirmDialog } from '../../components/ui/Dialog.jsx'
import { Checkbox, Field } from '../../components/ui/Field.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { AlbumStatusBadge, ErrorBanner, PageHeader } from '../../components/admin/AdminUi.jsx'
import { ClientSelect } from '../../components/admin/ClientSelect.jsx'
import { DriveFolderPicker } from '../../components/admin/DriveFolderPicker.jsx'
import { DriveUploadPanel } from '../../components/admin/DriveUploadPanel.jsx'
import { UploadQueue, acceptable, driveTarget, filesFromDrop } from '../../utils/driveUpload.js'
import { formatBytes, formatDate } from '../../utils/format.js'

const ROOT = { id: 'root', name: 'My Drive' }

const isHeic = (item) => /^image\/hei[cf]/i.test(item.mimeType) || /\.hei[cf]$/i.test(item.name)
const jpgUrl = (item) => `/api/admin/drive/items/${encodeURIComponent(item.id)}/content?download=true&format=jpg`
const canPreview = (item) => !item.isFolder && !item.isGoogleDoc && /^(image|video)\//.test(item.mimeType)
const contentUrl = (item, download = false) => `/api/admin/drive/items/${encodeURIComponent(item.id)}/content${download ? '?download=true' : ''}`
// Thumbnails are cached on the server per file version (its modified time, in .NET ticks).
const thumbUrl = (item, size = 'small') =>
  `/api/admin/drive/items/${encodeURIComponent(item.id)}/thumbnail?size=${size}&v=${item.modifiedTime ? Date.parse(item.modifiedTime) * 10000 + 621355968000000000 : 0}`

function typeIcon(item) {
  if (item.isFolder || item.shortcutToFolder) return '📁'
  if (item.mimeType.startsWith('image/')) return '🖼️'
  if (item.mimeType.startsWith('video/')) return '🎬'
  if (item.mimeType.includes('spreadsheet')) return '📊'
  if (item.mimeType.includes('presentation')) return '📽️'
  if (item.isGoogleDoc || item.mimeType.includes('pdf') || item.mimeType.startsWith('text/')) return '📄'
  return '📎'
}

/** Used / total storage of the studio's Google account, with what's sitting in the trash. */
function StorageBar({ version }) {
  const { data } = useApi('/api/admin/drive/storage', { v: version })
  if (!data) return null
  const pct = data.limit ? Math.min(100, Math.round((data.usage / data.limit) * 100)) : 0
  return (
    <div className={`adm-storage${pct >= 90 ? ' is-full' : ''}`}>
      <div className="adm-storage__text adm-small">
        <strong>{formatBytes(data.usage)}</strong> used {data.limit ? <>of {formatBytes(data.limit)}</> : '(unlimited plan)'}
        {data.usageInTrash > 0 && <span className="adm-muted"> · {formatBytes(data.usageInTrash)} in trash</span>}
        {data.email && <span className="adm-muted"> · {data.email}</span>}
      </div>
      {data.limit ? (
        <div className="adm-progress" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label="Drive storage used">
          <span style={{ width: `${pct}%` }} />
        </div>
      ) : null}
    </div>
  )
}

/** Large preview of a photo or video, with previous / next through the folder's previewable files. */
function Preview({ items, index, onIndex, onClose, onSetCover }) {
  const item = items[index]
  useEffect(() => {
    const onKey = (e) => {
      if (e.key === 'ArrowRight' && index < items.length - 1) onIndex(index + 1)
      if (e.key === 'ArrowLeft' && index > 0) onIndex(index - 1)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [index, items.length, onIndex])
  if (!item) return null
  return (
    <Dialog
      open
      wide
      onClose={onClose}
      title={item.name}
      footer={
        <>
          <button type="button" className="adm-btn" disabled={index === 0} onClick={() => onIndex(index - 1)}>
            ← Previous
          </button>
          <button type="button" className="adm-btn" disabled={index === items.length - 1} onClick={() => onIndex(index + 1)}>
            Next →
          </button>
          {onSetCover && item.hasThumbnail && (
            <button type="button" className="adm-btn" onClick={() => onSetCover(item)}>
              Set as album cover
            </button>
          )}
          {isHeic(item) && (
            <a className="adm-btn" href={contentUrl(item, true)}>
              Original (HEIC)
            </a>
          )}
          <a className="adm-btn adm-btn--primary" href={isHeic(item) ? jpgUrl(item) : contentUrl(item, true)}>
            {isHeic(item) ? 'Download as JPG' : 'Download'}
          </a>
        </>
      }
    >
      <div className="adm-preview">
        {item.mimeType.startsWith('video/') ? (
          <video key={item.id} src={contentUrl(item)} controls autoPlay preload="metadata" poster={item.hasThumbnail ? thumbUrl(item, 'large') : undefined} />
        ) : (
          <img key={item.id} src={item.hasThumbnail ? thumbUrl(item, 'large') : contentUrl(item)} alt={item.name} />
        )}
      </div>
      <p className="adm-muted adm-small">
        {formatBytes(item.size)}
        {item.width ? ` · ${item.width} × ${item.height}` : ''} · {formatDate(item.modifiedTime)}
      </p>
    </Dialog>
  )
}

/**
 * Turns a Drive folder into a client's album: pick the client, name the album, choose whether the client
 * can see it yet. The folder's sub-folders (Photos, Videos, …) become the tabs of the client's gallery.
 */
function LinkToClientDialog({ folder, onClose, onLinked }) {
  const [form, setForm] = useState({ clientId: '', title: folder.name, status: 'draft', downloadsEnabled: true })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  async function submit(e) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const album = await api('/api/admin/albums', {
        method: 'POST',
        body: { clientId: Number(form.clientId), title: form.title, status: form.status, downloadsEnabled: form.downloadsEnabled, driveFolder: folder.id },
      })
      onLinked(album)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog
      open
      onClose={onClose}
      title={`Link “${folder.name}” to a client`}
      footer={
        <button type="submit" form="link-client" className="adm-btn adm-btn--primary" disabled={busy || !form.clientId}>
          {busy ? 'Linking…' : 'Link and create album'}
        </button>
      }
    >
      <form id="link-client" className="adm-form" onSubmit={submit}>
        <ErrorBanner error={error} />
        <p className="adm-muted adm-small">
          This folder becomes the client’s album. Its sub-folders (e.g. Photos, Videos) become tabs in their gallery, and anything added to it later shows
          up for them too.
        </p>
        <ClientSelect value={form.clientId} onChange={(v) => setForm((f) => ({ ...f, clientId: v }))} error={error?.fieldErrors?.clientId} />
        <Field label="Album title (shown to the client)" required maxLength={200} value={form.title} onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))} />
        <Field label="Can the client see it?" as="select" value={form.status} onChange={(e) => setForm((f) => ({ ...f, status: e.target.value }))} error={error?.fieldErrors?.status}>
          <option value="draft">Not yet (draft) — publish when the upload is done</option>
          <option value="published">Yes, publish it now</option>
        </Field>
        <Checkbox
          label="Allow downloads"
          hint="When off, the client can view photos and stream videos but not download them."
          checked={form.downloadsEnabled}
          onChange={(e) => setForm((f) => ({ ...f, downloadsEnabled: e.target.checked }))}
        />
        {error?.fieldErrors?.driveFolder && <p className="field__error">{error.fieldErrors.driveFolder}</p>}
      </form>
    </Dialog>
  )
}

/** "Album · title · client · status" label for a folder that is a client's album. */
function AlbumTag({ album }) {
  return (
    <span className="adm-album-tag adm-small">
      <span className="adm-album-tag__label">Album</span>
      <Link to={`/admin/albums/${album.id}`}>
        {album.clientName}
      </Link>
      <AlbumStatusBadge status={album.status} />
    </span>
  )
}

/**
 * The studio's Google Drive inside the admin: browse My Drive / Shared with me, search, preview, download,
 * create folders, upload files or whole folders (resumable), rename, move and send to trash. Album folders
 * are ordinary folders here; changes in them re-sync the album.
 */
export default function DrivePage() {
  const [params, setParams] = useSearchParams()
  const view = params.get('view') === 'shared' ? 'shared' : 'mine'
  const folderId = params.get('folder') || 'root'
  const query = params.get('q') ?? ''
  const [searchText, setSearchText] = useState(query)

  const [version, setVersion] = useState(0) // bump to reload listing + storage
  const [pages, setPages] = useState({ items: [], next: null, folderAlbum: null, loading: true, error: null })
  const [selected, setSelected] = useState(() => new Set())
  const [preview, setPreview] = useState(-1)
  const [dialog, setDialog] = useState(null) // { type: 'folder' | 'rename' | 'move' | 'trash', item? }
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  const [notice, setNotice] = useState(null)
  const [dragging, setDragging] = useState(false)
  const [linking, setLinking] = useState(null) // folder being linked to a client
  const filesInput = useRef(null)
  const folderInput = useRef(null)

  const searching = query.trim().length > 0
  const inFolder = view === 'mine' && !searching
  const { data: trail } = useApi(inFolder && folderId !== 'root' ? `/api/admin/drive/items/${encodeURIComponent(folderId)}/trail` : null)
  const crumbs = folderId === 'root' || !trail ? [ROOT] : [ROOT, ...trail.slice(1)]
  const currentName = crumbs[crumbs.length - 1]?.name ?? 'My Drive'
  // The folder being viewed, to show whether it's a client's album.
  const { data: current } = useApi(inFolder && folderId !== 'root' ? `/api/admin/drive/items/${encodeURIComponent(folderId)}` : null, { v: version })

  // ── Listing (pages of 200, "Load more") ─────────────────────────────────
  const listQuery = useMemo(
    () => (searching ? { q: query.trim() } : view === 'shared' ? { view: 'shared' } : { parentId: folderId }),
    [searching, query, view, folderId],
  )

  useEffect(() => {
    const controller = new AbortController()
    setPages((p) => ({ ...p, loading: true, error: null }))
    setSelected(new Set())
    api('/api/admin/drive/items', { query: listQuery, signal: controller.signal })
      .then((data) => setPages({ items: data.items, next: data.nextPageToken, folderAlbum: data.folderAlbum, loading: false, error: null }))
      .catch((err) => err.name !== 'AbortError' && setPages({ items: [], next: null, folderAlbum: null, loading: false, error: err }))
    return () => controller.abort()
  }, [listQuery, version])

  async function loadMore() {
    setPages((p) => ({ ...p, loading: true }))
    try {
      const data = await api('/api/admin/drive/items', { query: { ...listQuery, pageToken: pages.next } })
      setPages((p) => ({ ...p, items: [...p.items, ...data.items], next: data.nextPageToken, loading: false, error: null }))
    } catch (err) {
      setPages((p) => ({ ...p, loading: false, error: err }))
    }
  }

  const reload = () => setVersion((v) => v + 1)
  const items = pages.items
  const previewable = items.filter(canPreview)

  // ── Navigation ──────────────────────────────────────────────────────────
  function go(next) {
    setSearchText(next.q ?? '')
    setParams(Object.fromEntries(Object.entries(next).filter(([, v]) => v && v !== 'root' && v !== 'mine')))
  }
  const openFolder = (id) => go({ folder: id })

  function open(item) {
    if (item.isFolder) return openFolder(item.id)
    if (item.shortcutToFolder) return openFolder(item.shortcutTargetId)
    if (canPreview(item)) return setPreview(previewable.indexOf(item))
    if (item.isGoogleDoc || item.shortcutTargetId) return window.open(item.webViewLink, '_blank', 'noopener')
    window.location.href = contentUrl(item, true)
  }

  // ── Uploads (into the folder being viewed) ──────────────────────────────
  const queueRef = useRef(null)
  const [, setTick] = useState(0)
  if (!queueRef.current) {
    let frame = 0
    queueRef.current = new UploadQueue(driveTarget, {
      onChange: () => {
        if (!frame)
          frame = requestAnimationFrame(() => {
            frame = 0
            setTick((t) => t + 1)
          })
      },
      onFinished: () => setVersion((v) => v + 1),
    })
  }
  const queue = queueRef.current

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

  /**
   * Uploads files into the current folder. Files from a dropped / chosen folder keep their structure: the
   * folder (and its sub-folders) are created in Drive first, then each file goes into its own folder.
   */
  async function upload(list) {
    if (!inFolder) return
    const files = list.filter(({ file }) => acceptable(file))
    if (!files.length) return
    setError(null)
    const folders = new Map([['', folderId]])
    try {
      for (const { path } of files) {
        const parts = path.split('/').slice(0, -1)
        for (let i = 1; i <= parts.length; i++) {
          const dir = parts.slice(0, i).join('/')
          if (folders.has(dir)) continue
          const created = await api('/api/admin/drive/folders', { method: 'POST', body: { name: parts[i - 1], parentId: folders.get(parts.slice(0, i - 1).join('/')) } })
          folders.set(dir, created.id)
        }
      }
    } catch (err) {
      setError(err)
      return
    }
    queue.add(
      files.map(({ file, path }) => {
        const dir = path.split('/').slice(0, -1).join('/')
        return { file, dest: folders.get(dir), destLabel: [currentName, dir].filter(Boolean).join(' / ') }
      }),
    )
    if (folders.size > 1) reload()
  }

  const fromInput = (e) => {
    upload([...e.target.files].map((file) => ({ file, path: file.webkitRelativePath || file.name })))
    e.target.value = ''
  }

  // ── Actions ─────────────────────────────────────────────────────────────
  const selectedItems = items.filter((i) => selected.has(i.id))
  const toggle = (id) =>
    setSelected((s) => {
      const next = new Set(s)
      next.has(id) ? next.delete(id) : next.add(id)
      return next
    })

  function startDialog(type, item = null) {
    setError(null)
    setName(type === 'rename' ? item.name : type === 'folder' ? 'New folder' : '')
    setDialog({ type, item })
  }

  async function run(action, success) {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const result = await action()
      if (result?.failed?.length) setError({ message: `${result.failed.length} item(s) failed: ${result.failed.map((f) => f.error).join('; ')}`, fieldErrors: {} })
      setNotice(typeof success === 'function' ? success(result) : success)
      setDialog(null)
      reload()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  // Files in an album's folder (or its Photos / Videos) can become that album's cover.
  const coverAlbum = inFolder ? pages.folderAlbum : null
  const setCover = (item) =>
    run(
      () => api(`/api/admin/drive/items/${encodeURIComponent(item.id)}/album-cover`, { method: 'PUT' }),
      (r) => `“${item.name}” is now the cover of “${r.albumTitle}” (${r.clientName}).`,
    )

  const createFolder = () =>
    run(() => api('/api/admin/drive/folders', { method: 'POST', body: { name, parentId: folderId } }), `Folder “${name}” created.`)
  const rename = () => run(() => api(`/api/admin/drive/items/${encodeURIComponent(dialog.item.id)}`, { method: 'PATCH', body: { name } }), 'Renamed.')
  const targets = () => (dialog?.item ? [dialog.item] : selectedItems)
  const move = (toFolderId, label) =>
    run(
      () => api('/api/admin/drive/move', { method: 'POST', body: { ids: targets().map((i) => i.id), toFolderId } }),
      (r) => `Moved ${r.done.length} item(s) to ${label ?? 'the folder'}.`,
    )
  const trash = () =>
    run(
      () => api('/api/admin/drive/trash', { method: 'POST', body: { ids: targets().map((i) => i.id) } }),
      (r) => `${r.done.length} item(s) moved to the Drive trash. You can restore them from Google Drive for 30 days.`,
    )

  const actionTargets = dialog?.item ? [dialog.item] : selectedItems

  return (
    <>
      <PageHeader title="Drive" subtitle="The studio’s Google Drive: browse, upload, organise. Album folders are here too." />
      <StorageBar version={version} />

      <div className="adm-drive-bar">
        <div className="adm-tabs" role="tablist" aria-label="Drive views">
          <button type="button" role="tab" aria-selected={view === 'mine' && !searching} onClick={() => go({})}>
            My Drive
          </button>
          <button type="button" role="tab" aria-selected={view === 'shared' && !searching} onClick={() => go({ view: 'shared' })}>
            Shared with me
          </button>
        </div>
        <form
          className="adm-drive-search"
          role="search"
          onSubmit={(e) => {
            e.preventDefault()
            go(searchText.trim() ? { q: searchText.trim() } : { folder: folderId })
          }}
        >
          <input type="search" className="adm-input" placeholder="Search all of Drive by name…" aria-label="Search Drive" value={searchText} onChange={(e) => setSearchText(e.target.value)} />
        </form>
      </div>

      <section
        className={`adm-card adm-drive${dragging ? ' is-dragging' : ''}`}
        onDragOver={(e) => {
          if (!inFolder) return
          e.preventDefault()
          setDragging(true)
        }}
        onDragLeave={(e) => !e.currentTarget.contains(e.relatedTarget) && setDragging(false)}
        onDrop={async (e) => {
          if (!inFolder) return
          e.preventDefault()
          setDragging(false)
          upload(await filesFromDrop(e.dataTransfer))
        }}
      >
        <header className="adm-drive__head">
          {searching ? (
            <h2 className="adm-drive__title">
              Results for “{query}”{' '}
              <button type="button" className="adm-link-btn adm-small" onClick={() => go({})}>
                Clear search
              </button>
            </h2>
          ) : view === 'shared' ? (
            <h2 className="adm-drive__title">Shared with me</h2>
          ) : (
            <nav className="adm-picker__trail adm-drive__title" aria-label="Folder path">
              {crumbs.map((c, i) => (
                <span key={c.id}>
                  {i > 0 && <span aria-hidden="true"> / </span>}
                  <button type="button" className="adm-link-btn" disabled={i === crumbs.length - 1} onClick={() => openFolder(c.id)}>
                    {c.name}
                  </button>
                </span>
              ))}
            </nav>
          )}

          <div className="adm-form__actions adm-drive__tools">
            {selected.size > 0 ? (
              <>
                <span className="adm-small">{selected.size} selected</span>
                <button type="button" className="adm-btn adm-btn--small" onClick={() => startDialog('move')}>
                  Move
                </button>
                <button type="button" className="adm-btn adm-btn--small adm-btn--danger-quiet" onClick={() => startDialog('trash')}>
                  Move to trash
                </button>
                <button type="button" className="adm-link-btn adm-small" onClick={() => setSelected(new Set())}>
                  Clear
                </button>
              </>
            ) : (
              inFolder && (
                <>
                  <button type="button" className="adm-btn adm-btn--small" onClick={() => startDialog('folder')}>
                    + New folder
                  </button>
                  <button type="button" className="adm-btn adm-btn--small adm-btn--primary" onClick={() => filesInput.current?.click()}>
                    Upload files
                  </button>
                  <button type="button" className="adm-btn adm-btn--small" onClick={() => folderInput.current?.click()}>
                    Upload folder
                  </button>
                </>
              )
            )}
          </div>
          <input ref={filesInput} type="file" multiple hidden onChange={fromInput} />
          <input ref={folderInput} type="file" multiple hidden webkitdirectory="" onChange={fromInput} />
        </header>

        {inFolder && current?.album && (
          <div className="adm-callout adm-album-banner">
            <span>
              📸 This folder is the album <strong>{current.album.title}</strong> for <strong>{current.album.clientName}</strong>{' '}
              <AlbumStatusBadge status={current.album.status} /> · {current.album.fileCount} files.{' '}
              {current.album.status !== 'published' && 'The client can’t see it until it’s published.'}
            </span>
            <Link to={`/admin/albums/${current.album.id}`} className="adm-btn adm-btn--small">
              Album settings
            </Link>
          </div>
        )}
        {inFolder && current && !current.album && (
          <div className="adm-callout adm-album-banner">
            <span className="adm-muted">This folder isn’t linked to a client.</span>
            <button type="button" className="adm-btn adm-btn--small" onClick={() => setLinking(current)}>
              Link this folder to a client
            </button>
          </div>
        )}
        <ErrorBanner error={error} />
        {notice && <p className="form-success">{notice}</p>}
        {dragging && <p className="adm-drive__drop">Drop to upload into “{currentName}”</p>}

        {pages.error?.status === 503 ? (
          <div className="adm-callout adm-callout--warn">
            {pages.error.message} <Link to="/admin/google-drive">Open Drive settings</Link>
          </div>
        ) : pages.error ? (
          <ErrorState error={pages.error} onRetry={reload} />
        ) : !pages.loading && items.length === 0 ? (
          <p className="adm-muted adm-drive__empty">
            {searching ? 'Nothing matches that name.' : view === 'shared' ? 'Nothing has been shared with this account.' : 'This folder is empty. Drag files here or use Upload.'}
          </p>
        ) : (
          <table className="adm-table adm-drive__table">
            <thead>
              <tr>
                <th className="adm-drive__check">
                  <input
                    type="checkbox"
                    aria-label="Select all"
                    checked={items.length > 0 && selected.size === items.length}
                    onChange={(e) => setSelected(e.target.checked ? new Set(items.map((i) => i.id)) : new Set())}
                  />
                </th>
                <th>Name</th>
                <th className="adm-drive__col-date">Modified</th>
                <th className="adm-drive__col-size">Size</th>
                <th aria-label="Actions" />
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id} className={selected.has(item.id) ? 'is-selected' : undefined}>
                  <td className="adm-drive__check">
                    <input type="checkbox" aria-label={`Select ${item.name}`} checked={selected.has(item.id)} onChange={() => toggle(item.id)} />
                  </td>
                  <td>
                    <button type="button" className="adm-drive__name" onClick={() => open(item)} title={item.name}>
                      <span className="adm-drive__thumb" aria-hidden="true">
                        {item.hasThumbnail && !item.isFolder ? <img src={thumbUrl(item)} alt="" loading="lazy" /> : <span>{typeIcon(item)}</span>}
                      </span>
                      <span className="adm-ellipsis">{item.name}</span>
                    </button>
                    {item.album && <AlbumTag album={item.album} />}
                  </td>
                  <td className="adm-drive__col-date adm-muted adm-small">{item.modifiedTime ? formatDate(item.modifiedTime) : ''}</td>
                  <td className="adm-drive__col-size adm-muted adm-small">{item.isFolder ? '—' : item.size ? formatBytes(item.size) : ''}</td>
                  <td className="adm-drive__actions">
                    <details className="adm-menu">
                      <summary aria-label={`Actions for ${item.name}`}>⋯</summary>
                      <div className="adm-menu__list" role="menu" onClick={(e) => e.currentTarget.closest('details')?.removeAttribute('open')}>
                        {item.album && (
                          <Link role="menuitem" to={`/admin/albums/${item.album.id}`}>
                            Open album ({item.album.clientName})
                          </Link>
                        )}
                        {item.isFolder && !item.album && (
                          <button type="button" role="menuitem" onClick={() => setLinking(item)}>
                            Link to client…
                          </button>
                        )}
                        {coverAlbum && item.hasThumbnail && /^(image|video)\//.test(item.mimeType) && (
                          <button type="button" role="menuitem" onClick={() => setCover(item)}>
                            Set as album cover
                          </button>
                        )}
                        {!item.isFolder && !item.isGoogleDoc && (
                          <a role="menuitem" href={contentUrl(item, true)}>
                            Download{isHeic(item) ? ' original (HEIC)' : ''}
                          </a>
                        )}
                        {isHeic(item) && (
                          <a role="menuitem" href={jpgUrl(item)}>
                            Download as JPG
                          </a>
                        )}
                        {item.canRename && (
                          <button type="button" role="menuitem" onClick={() => startDialog('rename', item)}>
                            Rename
                          </button>
                        )}
                        {item.canMove && (
                          <button type="button" role="menuitem" onClick={() => startDialog('move', item)}>
                            Move
                          </button>
                        )}
                        {item.webViewLink && (
                          <a role="menuitem" href={item.webViewLink} target="_blank" rel="noopener noreferrer">
                            Open in Google Drive ↗
                          </a>
                        )}
                        {item.canTrash && (
                          <button type="button" role="menuitem" className="adm-text-danger" onClick={() => startDialog('trash', item)}>
                            Move to trash
                          </button>
                        )}
                      </div>
                    </details>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {pages.loading && <Spinner />}
        {pages.next && !pages.loading && (
          <button type="button" className="adm-btn adm-drive__more" onClick={loadMore}>
            Load more
          </button>
        )}
        <p className="adm-muted adm-small">
          Tip: album folders work like any other folder here. Uploading, moving or trashing inside an album’s folder updates that album. Manage albums in{' '}
          <Link to="/admin/albums">Albums</Link>.
        </p>
      </section>

      <DriveUploadPanel queue={queue} />

      {linking && (
        <LinkToClientDialog
          folder={linking}
          onClose={() => setLinking(null)}
          onLinked={(album) => {
            setLinking(null)
            setNotice(`“${linking.name}” is now the album “${album.title}” for ${album.clientName}.`)
            reload()
          }}
        />
      )}

      {preview >= 0 && (
        <Preview
          items={previewable}
          index={preview}
          onIndex={setPreview}
          onClose={() => setPreview(-1)}
          onSetCover={
            coverAlbum
              ? (item) => {
                  setPreview(-1)
                  setCover(item)
                }
              : undefined
          }
        />
      )}

      <Dialog
        open={dialog?.type === 'folder' || dialog?.type === 'rename'}
        onClose={() => setDialog(null)}
        title={dialog?.type === 'folder' ? `New folder in “${currentName}”` : 'Rename'}
        footer={
          <button type="submit" form="drive-name" className="adm-btn adm-btn--primary" disabled={busy || !name.trim()}>
            {busy ? 'Saving…' : dialog?.type === 'folder' ? 'Create' : 'Rename'}
          </button>
        }
      >
        <form
          id="drive-name"
          className="adm-form"
          onSubmit={(e) => {
            e.preventDefault()
            dialog?.type === 'folder' ? createFolder() : rename()
          }}
        >
          <ErrorBanner error={error} />
          <Field label="Name" required autoFocus maxLength={255} value={name} onChange={(e) => setName(e.target.value)} />
        </form>
      </Dialog>

      <DriveFolderPicker
        open={dialog?.type === 'move'}
        onClose={() => setDialog(null)}
        onSelect={(id, label) => move(id, label)}
        title={`Move ${actionTargets.length === 1 ? `“${actionTargets[0]?.name}”` : `${actionTargets.length} items`} to…`}
        allowRoot
        hidePaste
      />

      <ConfirmDialog
        open={dialog?.type === 'trash'}
        title="Move to trash?"
        message={`${actionTargets.length === 1 ? `“${actionTargets[0]?.name}”` : `${actionTargets.length} items`} will go to the Google Drive trash. You can restore them from Google Drive for 30 days.${actionTargets.some((i) => i.isFolder) ? ' Folders go with everything inside them.' : ''}`}
        confirmLabel="Move to trash"
        danger
        busy={busy}
        onConfirm={trash}
        onCancel={() => setDialog(null)}
      />
    </>
  )
}
