import { Link, useSearchParams } from 'react-router'
import { useApi } from '../../hooks/useApi.js'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { AlbumStatusBadge, PageHeader, Pagination } from '../../components/admin/AdminUi.jsx'
import { formatBytes, formatDate, formatDateTime } from '../../utils/format.js'
import { DriveMoveBadge, OtherDrivesBanner, usePollWhile } from '../../components/admin/DriveMove.jsx'

export default function AlbumsPage() {
  const [params, setParams] = useSearchParams()
  const page = Number(params.get('page') ?? 1)
  const status = params.get('status') ?? ''
  const search = params.get('search') ?? ''
  const { data, error, loading, reload } = useApi('/api/admin/albums', { status, search, page, pageSize: 25 })
  const { data: drive, reload: reloadDrive } = useApi('/api/admin/google-drive/status')
  usePollWhile(data?.items.some((a) => a.driveMove?.state === 'queued' || a.driveMove?.state === 'running'), reload)
  const update = (patch) => setParams((p) => ({ ...Object.fromEntries(p), ...patch }))

  return (
    <>
      <PageHeader
        title="Albums"
        subtitle="Private client galleries. Each album shows the files of one Google Drive folder."
        actions={
          <Link to="/admin/albums/new" className="adm-btn adm-btn--primary">
            + New album
          </Link>
        }
      />
      <OtherDrivesBanner
        status={drive}
        onChanged={() => {
          reloadDrive()
          reload()
        }}
      />

      <div className="adm-filters">
        <input type="search" className="adm-input" placeholder="Search title or client…" aria-label="Search albums" defaultValue={search} onKeyDown={(e) => e.key === 'Enter' && update({ search: e.currentTarget.value, page: '1' })} onBlur={(e) => e.currentTarget.value !== search && update({ search: e.currentTarget.value, page: '1' })} />
        <select className="adm-input" aria-label="Status" value={status} onChange={(e) => update({ status: e.target.value, page: '1' })}>
          <option value="">All statuses</option>
          <option value="published">Published</option>
          <option value="draft">Draft</option>
          <option value="disabled">Disabled</option>
        </select>
      </div>

      {loading && !data && <Spinner />}
      {error && <ErrorState error={error} onRetry={reload} />}
      {data && (
        <>
          <div className="adm-table-wrap">
            <table className="adm-table">
              <thead>
                <tr>
                  <th>Album</th>
                  <th>Client</th>
                  <th>Files</th>
                  <th>Drive folder</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {data.items.length === 0 && (
                  <tr>
                    <td colSpan={5} className="adm-muted">
                      No albums found.
                    </td>
                  </tr>
                )}
                {data.items.map((a) => (
                  <tr key={a.id}>
                    <td>
                      <Link to={`/admin/albums/${a.id}`} className="adm-row-link">
                        {a.title}
                      </Link>
                      {a.eventDate && <div className="adm-muted adm-small">{formatDate(a.eventDate)}</div>}
                    </td>
                    <td>
                      <Link to={`/admin/clients/${a.clientId}`}>{a.clientName}</Link>
                    </td>
                    <td className="adm-small">
                      {a.fileCount} · {formatBytes(a.totalBytes)}
                    </td>
                    <td className="adm-small">
                      {a.lastSyncError ? (
                        <span className="adm-text-danger" title={a.lastSyncError}>
                          ⚠ {a.lastSyncError}
                        </span>
                      ) : a.driveFolderName ? (
                        <>
                          <div className="adm-ellipsis" title={a.driveFolderName}>
                            {a.driveFolderName}
                          </div>
                          {a.lastSyncedAt && <div className="adm-muted">Synced {formatDateTime(a.lastSyncedAt)}</div>}
                          <DriveMoveBadge move={a.driveMove} />
                        </>
                      ) : (
                        <span className="adm-muted">Not assigned</span>
                      )}
                    </td>
                    <td>
                      <AlbumStatusBadge status={a.status} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => update({ page: String(p) })} />
        </>
      )}
    </>
  )
}
