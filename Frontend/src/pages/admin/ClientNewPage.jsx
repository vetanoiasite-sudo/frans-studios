import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { api } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { Field, Checkbox } from '../../components/ui/Field.jsx'
import { CredentialsDialog, ErrorBanner, PageHeader } from '../../components/admin/AdminUi.jsx'
import { DriveFolderPicker } from '../../components/admin/DriveFolderPicker.jsx'

/** Suggests "sara.ahmed" from "Sara Ahmed". */
function suggestUsername(name) {
  return name
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/&/g, ' ')
    .replace(/[^a-z0-9]+/g, '.')
    .replace(/^\.+|\.+$/g, '')
    .slice(0, 64)
}

export default function ClientNewPage() {
  const navigate = useNavigate()
  const [form, setForm] = useState({ fullName: '', username: '', email: '', phone: '', notes: '', password: '' })
  const [usernameTouched, setUsernameTouched] = useState(false)
  const [customPassword, setCustomPassword] = useState(false)
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)
  const [created, setCreated] = useState(null)
  // The client's album: 'create' a new Drive folder, link an 'existing' folder, or do it 'later'.
  const { data: drive } = useApi('/api/admin/google-drive/status')
  const canCreateFolder = Boolean(drive?.canUpload)
  const [albumMode, setAlbumMode] = useState('create')
  const [albumTitle, setAlbumTitle] = useState('')
  const [titleTouched, setTitleTouched] = useState(false)
  const [folder, setFolder] = useState(null) // { id, label }
  const [picker, setPicker] = useState(false)
  const [album, setAlbum] = useState(null)
  const [albumError, setAlbumError] = useState(null)
  const mode = !drive?.connected ? 'later' : albumMode === 'create' && !canCreateFolder ? 'existing' : albumMode

  const set = (name) => (e) => {
    const value = e.target.value
    setForm((f) => ({
      ...f,
      [name]: value,
      ...(name === 'fullName' && !usernameTouched ? { username: suggestUsername(value) } : {}),
    }))
    if (name === 'fullName' && !titleTouched) setAlbumTitle(value ? `${value} — Wedding` : '')
  }

  async function submit(e) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const result = await api('/api/admin/clients', {
        method: 'POST',
        body: { ...form, password: customPassword ? form.password : null, email: form.email || null },
      })
      // Then the album. If that part fails the client still exists; the album can be made from the Drive page.
      if (mode === 'create' || (mode === 'existing' && folder)) {
        try {
          setAlbum(
            await api('/api/admin/albums', {
              method: 'POST',
              body: {
                clientId: result.clientId,
                title: albumTitle || form.fullName,
                status: 'draft',
                createDriveFolder: mode === 'create',
                driveFolder: mode === 'existing' ? folder.id : null,
              },
            }),
          )
        } catch (err) {
          setAlbumError(err)
        }
      }
      setCreated(result)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  const fe = (name) => error?.fieldErrors?.[name]

  return (
    <>
      <PageHeader title="New client" subtitle="Creates the client and their gallery login." back={<Link to="/admin/clients" className="adm-back">← Clients</Link>} />
      <form className="adm-card adm-form" onSubmit={submit}>
        <ErrorBanner error={error} />
        <Field label="Full name" placeholder="Sara & Ahmed" required value={form.fullName} onChange={set('fullName')} error={fe('fullName')} />
        <Field
          label="Username"
          hint="Used to sign in. Letters, numbers, dots, dashes."
          required
          autoCapitalize="none"
          spellCheck={false}
          value={form.username}
          onChange={(e) => {
            setUsernameTouched(true)
            set('username')(e)
          }}
          error={fe('username')}
        />
        <div className="adm-form__row">
          <Field label="Email (optional)" type="email" value={form.email} onChange={set('email')} error={fe('email')} />
          <Field label="Phone (optional)" type="tel" value={form.phone} onChange={set('phone')} error={fe('phone')} />
        </div>
        <Field label="Private notes (optional)" as="textarea" rows={3} value={form.notes} onChange={set('notes')} />
        <Checkbox label="Set the password myself" hint="Otherwise a strong password is generated for you." checked={customPassword} onChange={(e) => setCustomPassword(e.target.checked)} />
        {customPassword && <Field label="Password" type="text" autoComplete="new-password" minLength={10} value={form.password} onChange={set('password')} error={fe('password')} hint="At least 10 characters." />}

        <fieldset className="adm-folder-choice">
          <legend className="field__label">Album</legend>
          {drive && !drive.connected ? (
            <p className="adm-muted adm-small">
              Connect Google Drive in <Link to="/admin/google-drive">Drive settings</Link> to give the client an album. You can also add it later.
            </p>
          ) : (
            <>
              {canCreateFolder && (
                <label className="adm-radio">
                  <input type="radio" name="albumMode" value="create" checked={mode === 'create'} onChange={() => setAlbumMode('create')} />
                  <span>
                    <strong>Create a new album folder in Drive</strong>
                    <span className="adm-muted adm-small">
                      In <em>My Drive / Frans Studios Albums</em>, with Photos and Videos inside. Upload the photos to it afterwards.
                    </span>
                  </span>
                </label>
              )}
              <label className="adm-radio">
                <input type="radio" name="albumMode" value="existing" checked={mode === 'existing'} onChange={() => setAlbumMode('existing')} />
                <span>
                  <strong>Use a folder that’s already in Drive</strong>
                  <span className="adm-muted adm-small">For example the wedding you’ve already uploaded.</span>
                </span>
              </label>
              <label className="adm-radio">
                <input type="radio" name="albumMode" value="later" checked={mode === 'later'} onChange={() => setAlbumMode('later')} />
                <span>
                  <strong>Later</strong>
                  <span className="adm-muted adm-small">Link a folder to this client from the Drive page whenever it’s ready.</span>
                </span>
              </label>

              {mode === 'existing' && (
                <div className="adm-folder">
                  {folder ? (
                    <p className="adm-folder__picked">
                      <span aria-hidden="true">📁</span> <strong>{folder.label ?? 'Folder from the pasted link'}</strong>
                    </p>
                  ) : (
                    <p className="adm-muted adm-small">No folder chosen yet.</p>
                  )}
                  <button type="button" className="adm-btn adm-btn--small" onClick={() => setPicker(true)}>
                    {folder ? 'Change folder' : 'Choose Drive folder'}
                  </button>
                </div>
              )}
              {mode !== 'later' && (
                <Field
                  label="Album title (shown to the client)"
                  placeholder="Sara & Ahmed — Wedding"
                  maxLength={200}
                  value={albumTitle}
                  onChange={(e) => {
                    setTitleTouched(true)
                    setAlbumTitle(e.target.value)
                  }}
                  hint="The album starts hidden from the client (draft). Publish it from the album page when the photos are ready."
                />
              )}
            </>
          )}
        </fieldset>
        <div className="adm-form__actions">
          <button type="submit" className="adm-btn adm-btn--primary" disabled={busy}>
            {busy ? 'Creating…' : mode === 'later' ? 'Create client' : 'Create client and album'}
          </button>
        </div>
      </form>

      <DriveFolderPicker
        open={picker}
        onClose={() => setPicker(false)}
        onSelect={(id, label) => {
          setPicker(false)
          setFolder({ id, label })
        }}
      />

      {created && albumError && (
        <p className="form-error" role="alert">
          The client was created, but the album wasn’t: {albumError.fieldErrors?.driveFolder ?? albumError.message} You can link a folder from the Drive
          page.
        </p>
      )}
      <CredentialsDialog
        credentials={created}
        onClose={() => navigate(album ? `/admin/albums/${album.id}` : `/admin/clients/${created.clientId}`)}
        title={album ? 'Client and album created — login details' : 'Client created — login details'}
      />
    </>
  )
}
