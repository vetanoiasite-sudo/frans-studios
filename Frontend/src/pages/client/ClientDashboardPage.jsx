import { Navigate } from 'react-router'
import { useApi } from '../../hooks/useApi.js'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { useAuth } from '../../context/AuthContext.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { ClientAlbumCard } from '../../components/client/ClientAlbumCard.jsx'
import { t } from '../../i18n/index.js'

export default function ClientDashboardPage() {
  usePageTitle('Your galleries')
  const { user } = useAuth()
  const { data: albums, error, loading, reload } = useApi('/api/client/albums')

  if (loading) return <Spinner />
  if (error) return <ErrorState error={error} onRetry={reload} />
  // Most couples have a single gallery — take them straight to it.
  if (albums?.length === 1) return <Navigate to={`/client/albums/${albums[0].id}`} replace />

  return (
    <div className="cl-dashboard">
      <header className="cl-dashboard__head">
        <p className="cl-eyebrow">{t('client.dashboard.greeting', { name: user?.displayName ?? '' })}</p>
        <h1 className="cl-dashboard__title">Your galleries</h1>
        {albums?.length > 1 && (
          <p className="cl-dashboard__lead">
            {albums.length} galleries, kept safe for you. Choose one to view, play and download your photos and films.
          </p>
        )}
      </header>

      {albums?.length === 0 ? (
        <p className="cl-empty">{t('client.dashboard.empty')}</p>
      ) : (
        <ul className={`cl-cards${albums.length === 2 ? ' cl-cards--two' : ''}`} role="list">
          {albums.map((a) => (
            <li key={a.id}>
              <ClientAlbumCard album={a} />
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
