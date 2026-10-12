import { Link } from 'react-router'
import { useApi } from '../../hooks/useApi.js'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { PageHeader, StatusBadge } from '../../components/admin/AdminUi.jsx'
import { formatDateTime } from '../../utils/format.js'

export default function DashboardPage() {
  const { data, error, loading, reload } = useApi('/api/admin/dashboard')
  if (loading) return <Spinner />
  if (error) return <ErrorState error={error} onRetry={reload} />

  return (
    <>
      <PageHeader
        title="Overview"
        actions={
          <>
            <Link to="/admin/clients/new" className="adm-btn adm-btn--primary">
              + New client
            </Link>
            <Link to="/admin/albums/new" className="adm-btn">
              + New album
            </Link>
          </>
        }
      />

      {!data.driveConnected && (
        <div className="adm-callout adm-callout--warn">
          <strong>Google Drive is not connected.</strong> Client galleries can’t load files until you{' '}
          <Link to="/admin/google-drive">connect the studio’s Google Drive</Link>.
        </div>
      )}
      {data.albumsWithSyncErrors > 0 && (
        <div className="adm-callout adm-callout--danger">
          {data.albumsWithSyncErrors} album(s) couldn’t read their Drive folder. <Link to="/admin/albums">Review albums</Link>.
        </div>
      )}

      <div className="adm-stats">
        <Link to="/admin/clients?status=active" className="adm-stat">
          <span className="adm-stat__value">{data.activeClients}</span>
          <span className="adm-stat__label">Active clients</span>
        </Link>
        <Link to="/admin/albums?status=published" className="adm-stat">
          <span className="adm-stat__value">{data.publishedAlbums}</span>
          <span className="adm-stat__label">Published albums</span>
        </Link>
        <Link to="/admin/albums?status=draft" className="adm-stat">
          <span className="adm-stat__value">{data.draftAlbums}</span>
          <span className="adm-stat__label">Draft albums</span>
        </Link>
        <Link to="/admin/inquiries" className="adm-stat">
          <span className="adm-stat__value">{data.newInquiries}</span>
          <span className="adm-stat__label">New requests</span>
        </Link>
      </div>

      <section className="adm-card">
        <header className="adm-card__head">
          <h2>Recent requests</h2>
          <Link to="/admin/inquiries">View all</Link>
        </header>
        {data.recentInquiries.length === 0 ? (
          <p className="adm-muted">No requests yet.</p>
        ) : (
          <table className="adm-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Event</th>
                <th>Received</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {data.recentInquiries.map((i) => (
                <tr key={i.id}>
                  <td>
                    {i.name}{' '}
                    <span className="adm-muted adm-small">· {i.origin === 'contact' ? 'Contact' : 'Packages'}</span>
                  </td>
                  <td>{[i.eventType, i.eventDate].filter(Boolean).join(' · ') || '—'}</td>
                  <td>{formatDateTime(i.createdAt)}</td>
                  <td>
                    <StatusBadge tone={i.status === 'new' ? 'warn' : 'neutral'}>{i.status}</StatusBadge>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <p className="adm-muted adm-small">
        Google Drive: {data.driveConnected ? `connected as ${data.driveAccount}` : 'not connected'}
      </p>
    </>
  )
}
