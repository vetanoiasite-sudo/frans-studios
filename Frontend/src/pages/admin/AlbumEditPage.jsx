import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { api } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { Checkbox, Field } from '../../components/ui/Field.jsx'
import { ConfirmDialog } from '../../components/ui/Dialog.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { AlbumStatusBadge, ErrorBanner, PageHeader, Pagination } from '../../components/admin/AdminUi.jsx'
import { DriveFolderPicker, driveFolderUrl } from '../../components/admin/DriveFolderPicker.jsx'
import { DriveUploader } from '../../components/admin/DriveUploader.jsx'
import { ClientSelect } from '../../components/admin/ClientSelect.jsx'
import { DriveMoveNotice } from '../../components/admin/DriveMove.jsx'
import { formatBytes, formatDateTime, formatDuration } from '../../utils/format.js'

function AlbumFiles({ album, onCoverChanged }) {
  const [page, setPage] = useState(1)
  const { data, loading, error } = useApi(`/api/admin/albums/${album.id}/files`, { page, pageSize: 60, v: album.lastSyncedAt })
  const [busy, setBusy] = useState(null)

  async function setCover(fileId) {
    setBusy(fileId)
    try {
      await api(`/api/admin/albums/${album.id}/cover`, { method: 'PUT', body: { fileId } })
      onCoverChanged()
    } finally {
      setBusy(null)
    }
  }

  if (loading && !data) return <Spinner />
  if (error) return <p className="form-error">{error.message}</p>
  if (!data || data.total === 0) return <p className="adm-muted">No files indexed yet. Assign a folder and press “Sync now”.</p>

  return (
    <>
      <ul className="adm-thumbs" role="list">
        {data.items.map((f) => (
          <li key={f.id} className={f.id === album.coverFileId ? 'adm-thumb adm-thumb--cover' : 'adm-thumb'}>
            <a href={`/api/admin/files/${f.id}/content`} target="_blank" rel="noopener noreferrer" className="adm-thumb__img" title={f.name}>
              {f.hasThumbnail ? <img src={`/api/admin/files/${f.id}/thumbnail?size=small`} alt="" loading="lazy" /> : <span>{f.name.split('.').pop()?.toUpperCase()}</span>}
            </a>
            <div className="adm-thumb__meta">
              <span className="adm-ellipsis" title={`${f.section ? f.section + '/' : ''}${f.name}`}>
                {f.name}
              </span>
              <span className="adm-muted">
                {formatBytes(f.sizeBytes)}
                {f.durationMs ? ` · ${formatDuration(f.durationMs)}` : ''}
              </span>
            </div>
            {(f.kind === 'image' || (f.kind === 'video' && f.hasThumbnail)) &&
              (f.id === album.coverFileId ? (
                <span className="adm-thumb__cover">Cover</span>
              ) : (
                <button type="button" className="adm-link-btn adm-small" disabled={busy === f.id} onClick={() => setCover(f.id)}>
                  Set as cover
                </button>
              ))}
          </li>
        ))}
      </ul>
      <Pagination page={data.page} pageSize={data.pageSize} total={data.total} onPage={setPage} />
    </>
  )
}

/** "New album" and each album share this route component; a key per album gives each a fresh form. */
export default function AlbumEditRoute() {
  const { id } = useParams()
  return <AlbumEditPage key={id ?? 'new'} />
}

function AlbumEditPage() {
  const { id } = useParams()
  const isNew = !id
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const { data: album, error: loadError, loading, reload } = useApi(isNew ? null : `/api/admin/albums/${id}`)
  const { data: drive } = useApi('/api/admin/google-drive/status')
  // New album folders can be created in Drive once Drive is connected with upload access.
  const canCreateFolder = Boolean(drive?.canUpload)

  const [form, setForm] = useState({
    clientId: params.get('clientId') ?? '',
    title: '',
    description: '',
    eventDate: '',
    status: 'draft',
    downloadsEnabled: true,
    driveFolder: '',
    driveFolderLabel: null, // display path of the picked folder (null for a pasted link)
    folderMode: 'create', // new album: 'create' a Drive folder for it, or 'choose' an existing one
  })
  const [error, setError] = useState(null)
  const [notice, setNotice] = useState(null)
  const [busy, setBusy] = useState(false)
  const [picker, setPicker] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  useEffect(() => {
    if (album)
      setForm({
        clientId: String(album.clientId),
        title: album.title,
        description: album.description ?? '',
        eventDate: album.eventDate ?? '',
        status: album.status,
        downloadsEnabled: album.downloadsEnabled,
        driveFolder: album.driveFolderId ?? '',
        driveFolderLabel: album.driveFolderName,
        folderMode: 'choose',
      })
  }, [album])

  if (loadError) return <ErrorState error={loadError} onRetry={reload} />
  if (!isNew && (loading || !album)) return <Spinner />

  const creatingFolder = isNew && canCreateFolder && form.folderMode === 'create'
  const set = (name) => (e) => setForm((f) => ({ ...f, [name]: e.target.type === 'checkbox' ? e.target.checked : e.target.value }))
  const body = () => ({
    clientId: Number(form.clientId),
    title: form.title,
    description: form.description || null,
    eventDate: form.eventDate || null,
    status: form.status,
    downloadsEnabled: form.downloadsEnabled,
    driveFolder: isNew && !creatingFolder ? form.driveFolder || null : undefined,
    createDriveFolder: isNew ? creatingFolder : undefined,
  })

  async function save(e) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      if (isNew) {
        const created = await api('/api/admin/albums', { method: 'POST', body: body() })
        navigate(`/admin/albums/${created.id}`, { replace: true })
      } else {
        await api(`/api/admin/albums/${id}`, { method: 'PUT', body: body() })
        setNotice('Saved.')
        reload()
      }
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  async function chooseFolder(folder, label) {
    setPicker(false)
    if (isNew) {
      setForm((f) => ({ ...f, driveFolder: folder, driveFolderLabel: label }))
      return
    }
    setBusy(true)
    setError(null)
    try {
      await api(`/api/admin/albums/${id}/drive-folder`, { method: 'PUT', body: { folder } })
      setNotice('Folder assigned and files indexed.')
      reload()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  async function createFolder() {
    setBusy(true)
    setError(null)
    try {
      await api(`/api/admin/albums/${id}/drive-folder/create`, { method: 'POST' })
      setNotice('Folder created in Google Drive, with Photos and Videos inside.')
      reload()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  /** After a batch of uploads: index the new files now instead of waiting for the next automatic sync. */
  async function afterUpload() {
    try {
      await api(`/api/admin/albums/${id}/sync`, { method: 'POST' })
    } catch {
      /* the background sync queued by each upload catches up anyway */
    }
    reload()
  }

  async function sync() {
    setBusy(true)
    setError(null)
    try {
      const result = await api(`/api/admin/albums/${id}/sync`, { method: 'POST' })
      setNotice(result.success ? `Synced: ${result.fileCount} files (${formatBytes(result.totalBytes)}).` : null)
      if (!result.success) setError({ message: result.error, fieldErrors: {} })
      reload()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    setBusy(true)
    try {
      await api(`/api/admin/albums/${id}`, { method: 'DELETE' })
      navigate('/admin/albums', { replace: true })
    } catch (err) {
      setError(err)
      setBusy(false)
      setConfirmDelete(false)
    }
  }

  const fe = (name) => error?.fieldErrors?.[name]

  return (
    <>
      <PageHeader
        title={isNew ? 'New album' : album.title}
        subtitle={
          !isNew && (
            <>
              <AlbumStatusBadge status={album.status} /> · <Link to={`/admin/clients/${album.clientId}`}>{album.clientName}</Link> · {album.fileCount} files ·{' '}
              {formatBytes(album.totalBytes)}
            </>
          )
        }
        back={<Link to="/admin/albums" className="adm-back">← Albums</Link>}
      />

      {!isNew && <DriveMoveNotice album={album} connectedEmail={drive?.accountEmail} onChanged={reload} />}

      {/* Uploading is the main job once an album has its Drive folder, so it comes first. Not while it's in another account's Drive. */}
      {!isNew && album.driveFolderId && drive?.canUpload && !album.driveMove && <DriveUploader album={album} onUploaded={afterUpload} />}

      <div className="adm-columns">
        <form className="adm-card adm-form" onSubmit={save}>
          <h2>Album details</h2>
          <ErrorBanner error={error} />
          {notice && <p className="form-success">{notice}</p>}
          <ClientSelect value={form.clientId} onChange={(v) => setForm((f) => ({ ...f, clientId: v }))} error={fe('clientId')} />
          <Field label="Title" placeholder="Sara & Ahmed — Wedding" required value={form.title} onChange={set('title')} error={fe('title')} />
          <Field label="Description (shown to the client)" as="textarea" rows={3} value={form.description} onChange={set('description')} />
          <div className="adm-form__row">
            <Field label="Event date" type="date" value={form.eventDate} onChange={set('eventDate')} />
            <Field label="Status" as="select" value={form.status} onChange={set('status')} error={fe('status')}>
              <option value="draft">Draft — hidden from client</option>
              <option value="published">Published — visible to client</option>
              <option value="disabled">Disabled — access turned off</option>
            </Field>
          </div>
          <Checkbox label="Allow downloads" hint="When off, the client can view photos and stream videos but not download them." checked={form.downloadsEnabled} onChange={set('downloadsEnabled')} />

          {isNew && (
            <fieldset className="adm-folder adm-folder-choice">
              <legend className="field__label">Google Drive folder</legend>
              {canCreateFolder && (
                <>
                  <label className="adm-radio">
                    <input type="radio" name="folderMode" value="create" checked={form.folderMode === 'create'} onChange={set('folderMode')} />
                    <span>
                      <strong>Create a new folder for this album</strong>
                      <span className="adm-muted adm-small">
                        In <em>My Drive / Frans Studios Albums</em>, named after the album, with Photos and Videos inside. Upload the files here after creating
                        the album, or straight into Drive.
                      </span>
                    </span>
                  </label>
                  <label className="adm-radio">
                    <input type="radio" name="folderMode" value="choose" checked={form.folderMode === 'choose'} onChange={set('folderMode')} />
                    <span>
                      <strong>Use a folder that’s already in Drive</strong>
                    </span>
                  </label>
                </>
              )}
              {!creatingFolder && (
                <>
                  {form.driveFolder ? (
                    <p className="adm-folder__picked">
                      <span aria-hidden="true">📁</span> <strong>{form.driveFolderLabel ?? 'Folder from the pasted link'}</strong>
                    </p>
                  ) : (
                    <p className="adm-muted">No folder chosen yet (you can add it later).</p>
                  )}
                  <button type="button" className="adm-btn" onClick={() => setPicker(true)}>
                    {form.driveFolder ? 'Change folder' : 'Choose Drive folder'}
                  </button>
                </>
              )}
              {!canCreateFolder && drive?.connected && (
                <p className="adm-muted adm-small">
                  To have the site create album folders, {drive.canUpload ? 'choose where they go' : 'reconnect Drive with upload access'} in{' '}
                  <Link to="/admin/google-drive">Google Drive</Link>.
                </p>
              )}
              {fe('driveFolder') && <p className="field__error">{fe('driveFolder')}</p>}
            </fieldset>
          )}

          <div className="adm-form__actions">
            <button type="submit" className="adm-btn adm-btn--primary" disabled={busy}>
              {busy ? 'Saving…' : isNew ? 'Create album' : 'Save changes'}
            </button>
            {!isNew && (
              <button type="button" className="adm-btn adm-btn--danger-quiet" onClick={() => setConfirmDelete(true)}>
                Delete album
              </button>
            )}
          </div>
        </form>

        {!isNew && (
          <section className="adm-card adm-folder">
            <h2>Google Drive folder</h2>
            {album.driveFolderName ? (
              <p>
                <span aria-hidden="true">📁</span> {album.driveFolderName}{' '}
                <Link to={`/admin/drive?folder=${encodeURIComponent(album.driveFolderId)}`} className="adm-small">
                  Manage files
                </Link>{' '}
                ·{' '}
                <a href={driveFolderUrl(album.driveFolderId)} target="_blank" rel="noopener noreferrer" className="adm-small">
                  Open in Google Drive ↗
                </a>
              </p>
            ) : (
              <p className="adm-muted">No folder assigned. The album can’t be published until it has one.</p>
            )}
            {album.lastSyncError && <p className="form-error">{album.lastSyncError}</p>}
            {album.lastSyncedAt && <p className="adm-muted adm-small">Last synced {formatDateTime(album.lastSyncedAt)}</p>}
            <div className="adm-form__actions">
              {!album.driveFolderId && canCreateFolder && (
                <button type="button" className="adm-btn adm-btn--primary" onClick={createFolder} disabled={busy}>
                  Create folder in Drive
                </button>
              )}
              <button type="button" className="adm-btn" onClick={() => setPicker(true)} disabled={busy}>
                {album.driveFolderId ? 'Change folder' : 'Choose Drive folder'}
              </button>
              {album.driveFolderId && (
                <button type="button" className="adm-btn" onClick={sync} disabled={busy}>
                  {busy ? 'Working…' : 'Sync now'}
                </button>
              )}
            </div>
            <p className="adm-muted adm-small">
              Clients see new files automatically within about 15 minutes, or immediately after “Sync now”. Tip: upload a 1080p “web” version of each film next to the
              original so it streams smoothly on phones.
            </p>
          </section>
        )}
      </div>

      {!isNew && album.driveFolderId && drive?.connected && !drive.canUpload && (
        <section className="adm-card">
          <div className="adm-callout adm-callout--warn">
            Google Drive is connected with read-only access, so files can’t be uploaded from here yet. <Link to="/admin/google-drive">Reconnect Google Drive</Link> to
            allow uploads. Files the studio puts in the album’s Drive folder by hand still appear after “Sync now”.
          </div>
        </section>
      )}

      {!isNew && album.sections.length > 0 && (
        <section className="adm-card">
          <header className="adm-card__head">
            <h2>Files</h2>
            <span className="adm-muted adm-small">{album.sections.map((s) => `${s.name || 'Main folder'} (${s.count})`).join(' · ')}</span>
          </header>
          <AlbumFiles album={album} onCoverChanged={reload} />
        </section>
      )}

      <DriveFolderPicker open={picker} onClose={() => setPicker(false)} onSelect={chooseFolder} />
      <ConfirmDialog
        open={confirmDelete}
        title="Delete album?"
        message="The album is removed from the client’s gallery. The Google Drive folder and its files are not touched."
        confirmLabel="Delete album"
        danger
        busy={busy}
        onConfirm={remove}
        onCancel={() => setConfirmDelete(false)}
      />
    </>
  )
}
