import { t } from '../../i18n/index.js'

export function Spinner({ fullScreen = false, label = t('common.loading') }) {
  return (
    <div className={fullScreen ? 'spinner spinner--full' : 'spinner'} role="status" aria-live="polite">
      <span className="spinner__dot" aria-hidden="true" />
      <span className="visually-hidden">{label}</span>
    </div>
  )
}

export function ErrorState({ error, onRetry, message }) {
  return (
    <div className="error-state" role="alert">
      <p>{message ?? error?.message ?? t('common.error')}</p>
      {onRetry && (
        <button type="button" className="btn btn--ghost" onClick={onRetry}>
          {t('common.retry')}
        </button>
      )}
    </div>
  )
}
