import { isRouteErrorResponse, Link, useRouteError } from 'react-router'
import { t } from '../i18n/index.js'

/** Shown if a route fails to load (e.g. a chunk 404s after a deploy) or throws while rendering. */
export function RouteError() {
  const error = useRouteError()
  const chunkFailed = error instanceof TypeError && /dynamically imported module|Importing a module script failed/i.test(error.message)
  const notFound = isRouteErrorResponse(error) && error.status === 404

  return (
    <main className="route-error">
      <h1>{notFound ? t('common.notFound') : t('common.error')}</h1>
      {chunkFailed ? (
        <button type="button" className="btn" onClick={() => window.location.reload()}>
          {t('common.retry')}
        </button>
      ) : (
        <Link className="btn" to="/">
          {t('common.backHome')}
        </Link>
      )}
    </main>
  )
}
