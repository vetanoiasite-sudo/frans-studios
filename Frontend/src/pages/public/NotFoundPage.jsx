import { Link } from 'react-router'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { t } from '../../i18n/index.js'

export default function NotFoundPage() {
  usePageTitle('Page not found')
  return (
    <div className="route-error">
      <p className="eyebrow">404</p>
      <h1>{t('common.notFound')}</h1>
      <Link to="/" className="btn">
        {t('common.backHome')}
      </Link>
    </div>
  )
}
