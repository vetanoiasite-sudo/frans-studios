import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { Lightbox } from '../../components/media/Lightbox.jsx'
import { ClientAlbumCard, albumSummary } from '../../components/client/ClientAlbumCard.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { fileExtension, formatBytes, formatDate, formatDuration } from '../../utils/format.js'
import { t } from '../../i18n/index.js'

const PAGE_SIZE = 200
const fileUrl = (id, mode) => `/api/client/files/${id}/content?mode=${mode}`
const thumbUrl = (id, size = 'small') => `/api/client/files/${id}/thumbnail?size=${size}`
// iPhone photos (HEIC/HEIF) download as JPEG by default — many phones and PCs can't open HEIC; the original stays available.
const isHeic = (f) => /^image\/hei[cf]/i.test(f.mimeType) || /\.hei[cf]$/i.test(f.name)
const downloadUrl = (f) => fileUrl(f.id, 'download') + (isHeic(f) ? '&format=jpg' : '')

/** Loads an album's file index page by page as the visitor scrolls. */
function useAlbumFiles(albumId, section) {
  const [state, setState] = useState({ items: [], total: null, page: 0, loading: false, error: null })
  const busy = useRef(false)
  // Bumped whenever the album/section changes so late responses for the old view are ignored.
  const generation = useRef(0)

  useEffect(() => {
    generation.current += 1
    setState({ items: [], total: null, page: 0, loading: false, error: null })
    busy.current = false
  }, [albumId, section])

  const loadMore = useCallback(async () => {
    if (busy.current) return
    busy.current = true
    const gen = generation.current
    setState((s) => ({ ...s, loading: true, error: null }))
    try {
      const page = state.page + 1
      const data = await api(`/api/client/albums/${albumId}/files`, {
        query: { page, pageSize: PAGE_SIZE, section: section ?? undefined },
      })
      if (gen !== generation.current) return
      setState((s) => ({ items: [...s.items, ...data.items], total: data.total, page, loading: false, error: null }))
    } catch (error) {
      if (gen === generation.current) setState((s) => ({ ...s, loading: false, error }))
    } finally {
      if (gen === generation.current) busy.current = false
    }
  }, [albumId, section, state.page])

  const hasMore = state.total === null || state.items.length < state.total
  return { ...state, hasMore, loadMore }
}

function Sentinel({ onVisible, disabled }) {
  const ref = useRef(null)
  useEffect(() => {
    if (disabled || !ref.current) return
    const io = new IntersectionObserver((entries) => entries[0].isIntersecting && onVisible(), { rootMargin: '1200px 0px' })
    io.observe(ref.current)
    return () => io.disconnect()
  }, [onVisible, disabled])
  return <div ref={ref} aria-hidden="true" className="cl-sentinel" />
}

function DownloadIcon() {
  return (
    <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
      <path d="M12 4v11m0 0-4.5-4.5M12 15l4.5-4.5M5 19h14" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

/**
 * "Gallery: <title> ▾" — switches between the client's galleries without going back to the list.
 * Each entry shows the cover and the date/counts.
 */
function AlbumSwitcher({ albums, currentId }) {
  const current = albums.find((a) => a.id === currentId)
  return (
    <details className="cl-switcher">
      <summary className="cl-switcher__toggle">
        <span className="cl-switcher__label">Gallery</span>
        <span className="cl-switcher__current">{current?.title}</span>
        <svg viewBox="0 0 16 16" width="12" height="12" aria-hidden="true">
          <path d="M3 6l5 5 5-5" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </summary>
      <div className="cl-switcher__menu" onClick={(e) => e.currentTarget.closest('details')?.removeAttribute('open')}>
        {albums.map((a) => (
          <Link key={a.id} to={`/client/albums/${a.id}`} className="cl-switcher__item" aria-current={a.id === currentId ? 'page' : undefined}>
            <span className="cl-switcher__thumb">{a.coverFileId && <img src={thumbUrl(a.coverFileId)} alt="" loading="lazy" />}</span>
            <span className="cl-switcher__text">
              <strong>{a.title}</strong>
              <span>{albumSummary(a)}</span>
            </span>
          </Link>
        ))}
        <Link to="/client" className="cl-switcher__all">
          {t('client.album.back')} →
        </Link>
      </div>
    </details>
  )
}

function MediaTile({ file, onOpen, downloads }) {
  // Aspect ratio drives a CSS-only justified layout (see .cl-tile in client.css).
  const ratio = file.width && file.height ? Math.min(Math.max(file.width / file.height, 0.4), 3) : 1.5
  const isVideo = file.kind === 'video'
  return (
    <li className="cl-tile" style={{ '--r': ratio }}>
      <button type="button" className="cl-tile__open" onClick={onOpen} aria-label={`${isVideo ? t('client.album.play') : t('client.album.view')}: ${file.name}`}>
        {file.hasThumbnail ? (
          <img src={thumbUrl(file.id)} alt="" loading="lazy" decoding="async" />
        ) : (
          <span className="cl-tile__fallback">{fileExtension(file.name)}</span>
        )}
        {isVideo && (
          <span className="cl-tile__video" aria-hidden="true">
            <svg viewBox="0 0 24 24" width="16" height="16">
              <path d="M8 5v14l11-7z" fill="currentColor" />
            </svg>
            {formatDuration(file.durationMs)}
          </span>
        )}
      </button>
      {downloads && (
        <a className="cl-tile__download" href={downloadUrl(file)} download aria-label={`${t('client.album.download')} ${file.name}`}>
          <DownloadIcon />
        </a>
      )}
    </li>
  )
}

export default function ClientAlbumPage() {
  const { id } = useParams()
  const { data: album, error, loading, reload } = useApi(`/api/client/albums/${id}`)
  const { data: me } = useApi('/api/client/me')
  const { data: allAlbums } = useApi(me?.albumCount > 1 ? '/api/client/albums' : null)
  const otherAlbums = allAlbums?.filter((a) => a.id !== id) ?? []
  const [section, setSection] = useState(null)
  const [viewer, setViewer] = useState(-1)
  usePageTitle(album?.title)

  const files = useAlbumFiles(id, section)
  const media = useMemo(() => files.items.filter((f) => f.kind !== 'other'), [files.items])
  const others = useMemo(() => files.items.filter((f) => f.kind === 'other'), [files.items])

  // Start loading the first page as soon as the album is known.
  useEffect(() => {
    if (album && files.page === 0 && !files.loading && !files.error) files.loadMore()
  }, [album, files])

  const slides = useMemo(
    () =>
      media.map((f) =>
        f.kind === 'video'
          ? { type: 'video', width: f.width ?? 1920, height: f.height ?? 1080, poster: f.hasThumbnail ? thumbUrl(f.id, 'large') : undefined, sources: [{ src: fileUrl(f.id, 'stream'), type: f.mimeType === 'video/quicktime' ? 'video/mp4' : f.mimeType }] }
          : { src: thumbUrl(f.id, 'large'), width: f.width ?? 2048, height: f.height ?? 1365, alt: f.name },
      ),
    [media],
  )

  if (loading) return <Spinner />
  if (error) return <ErrorState error={error} onRetry={error.status === 404 ? undefined : reload} message={error.status === 404 ? t('common.notFound') : undefined} />
  if (!album) return null

  const counts = [
    album.photoCount > 0 && t('client.album.photos', { count: album.photoCount }),
    album.videoCount > 0 && t('client.album.videos', { count: album.videoCount }),
    album.otherCount > 0 && t('client.album.files', { count: album.otherCount }),
  ].filter(Boolean)
  const namedSections = album.sections.filter((s) => s.name !== '')
  const zipSection = section ?? (namedSections.length === 0 ? '' : null)
  const currentFile = viewer >= 0 ? media[viewer] : null

  return (
    <article className="cl-album">
      <header className="cl-cover">
        {album.coverFileId && <img className="cl-cover__img" src={thumbUrl(album.coverFileId, 'large')} alt="" fetchPriority="high" />}
        <div className="cl-cover__content">
          {album.eventDate && <p className="eyebrow eyebrow--light">{formatDate(album.eventDate)}</p>}
          <h1 className="cl-cover__title">{album.title}</h1>
          {counts.length > 0 && <p className="cl-cover__counts">{counts.join(' · ')} · {formatBytes(album.totalBytes)}</p>}
        </div>
      </header>

      {album.description && <p className="cl-album__description">{album.description}</p>}

      <div className="cl-toolbar">
        <div className="cl-toolbar__inner">
          {allAlbums?.length > 1 && <AlbumSwitcher albums={allAlbums} currentId={id} />}
          {namedSections.length > 0 && (
            <nav className="cl-tabs" aria-label="Sections">
              <button type="button" className="cl-tab" aria-pressed={section === null} onClick={() => setSection(null)}>
                {t('client.album.all')} <span>{album.fileCount}</span>
              </button>
              {namedSections.map((s) => (
                <button key={s.name} type="button" className="cl-tab" aria-pressed={section === s.name} onClick={() => setSection(s.name)}>
                  {s.name} <span>{s.count}</span>
                </button>
              ))}
            </nav>
          )}
          {album.downloadsEnabled && zipSection !== null && (
            <a className="btn btn--ghost btn--small cl-toolbar__zip" href={`/api/client/albums/${album.id}/zip?section=${encodeURIComponent(zipSection)}`} download>
              <DownloadIcon />
              <span className="cl-toolbar__zip-label">{zipSection === '' ? t('client.album.downloadAll') : t('client.album.downloadSection', { section: zipSection })}</span>
            </a>
          )}
        </div>
      </div>

      {album.syncing && <p className="cl-note">{t('client.album.updating')}</p>}
      {!album.downloadsEnabled && <p className="cl-note">{t('client.album.downloadsOff')}</p>}
      {album.downloadsEnabled && album.totalBytes > 4 * 1024 ** 3 && <p className="cl-note">{t('client.album.largeZip')}</p>}
      {album.fileCount === 0 && <p className="cl-empty">{t('client.album.empty')}</p>}

      {media.length > 0 && (
        <ul className="cl-grid" role="list">
          {media.map((f, i) => (
            <MediaTile key={f.id} file={f} downloads={album.downloadsEnabled} onOpen={() => setViewer(i)} />
          ))}
        </ul>
      )}

      {others.length > 0 && (
        <section className="cl-files" aria-labelledby="other-files">
          <h2 id="other-files" className="cl-files__title">
            {t('client.album.otherFiles')}
          </h2>
          <ul role="list">
            {others.map((f) => (
              <li key={f.id} className="cl-file">
                <span className="cl-file__ext">{fileExtension(f.name)}</span>
                <span className="cl-file__name">{f.name}</span>
                <span className="cl-file__size">{formatBytes(f.sizeBytes)}</span>
                {album.downloadsEnabled && (
                  <a className="btn btn--ghost btn--small" href={fileUrl(f.id, 'download')} download>
                    {t('client.album.download')}
                  </a>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}

      {files.error && <ErrorState error={files.error} onRetry={files.loadMore} />}
      {files.loading && <Spinner />}
      <Sentinel onVisible={files.loadMore} disabled={!files.hasMore || files.loading || Boolean(files.error) || files.page === 0} />

      {otherAlbums.length > 0 && !files.hasMore && (
        <section className="cl-more" aria-labelledby="more-galleries">
          <h2 id="more-galleries" className="cl-more__title">
            Your other galleries
          </h2>
          <ul className="cl-cards cl-cards--compact" role="list">
            {otherAlbums.map((a) => (
              <li key={a.id}>
                <ClientAlbumCard album={a} compact />
              </li>
            ))}
          </ul>
        </section>
      )}

      <Lightbox
        open={viewer >= 0}
        slides={slides}
        index={Math.max(viewer, 0)}
        onClose={() => setViewer(-1)}
        onIndexChange={setViewer}
        renderToolbar={
          album.downloadsEnabled && currentFile
            ? () => (
                <>
                  {isHeic(currentFile) && (
                    <a key="original" className="yarl__button cl-lightbox-original" href={fileUrl(currentFile.id, 'download')} download title={`Original (HEIC) — ${formatBytes(currentFile.sizeBytes)}`}>
                      HEIC
                    </a>
                  )}
                  <a
                    key="download"
                    className="yarl__button cl-lightbox-download"
                    href={downloadUrl(currentFile)}
                    download
                    title={isHeic(currentFile) ? `${t('client.album.download')} (JPG)` : `${t('client.album.download')} — ${formatBytes(currentFile.sizeBytes)}`}
                  >
                    <DownloadIcon />
                    <span className="visually-hidden">{t('client.album.download')}</span>
                  </a>
                </>
              )
            : undefined
        }
      />
    </article>
  )
}
