import { useApi } from '../../hooks/useApi.js'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { VideoTile } from '../../components/media/VideoTile.jsx'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { t } from '../../i18n/index.js'

/** Poster with player chrome; the video file only downloads when the visitor presses play. */
function Film({ film }) {
  return (
    <article className="film">
      <VideoTile film={film} sizes="(min-width: 1200px) 1100px, 100vw" />
      <div className="film__meta">
        <h2 className="film__title">{film.title}</h2>
        {film.description && <p className="film__desc">{film.description}</p>}
      </div>
    </article>
  )
}

export default function FilmsPage() {
  usePageTitle('Wedding Films')
  const { data, error, loading, reload } = useApi('/api/public/films')
  return (
    <div className="pub-page">
      <header className="page-head">
        <p className="eyebrow">Cinematography</p>
        <h1 className="page-head__title">Wedding films</h1>
        <p className="page-head__lead">Through the lens of cinematic artistry we craft films that narrate your love story.</p>
      </header>
      {loading && <Spinner />}
      {error && <ErrorState error={error} onRetry={reload} />}
      {data?.length === 0 && <p className="empty-note">{t('films.empty')}</p>}
      <div className="film-list">{data?.map((f) => <Film key={f.id} film={f} />)}</div>
    </div>
  )
}
