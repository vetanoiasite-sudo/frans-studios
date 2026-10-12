import { useState } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router'
import { useAuth } from '../../context/AuthContext.jsx'
import { Field } from '../../components/ui/Field.jsx'
import { Spinner } from '../../components/ui/Spinner.jsx'
import { usePageTitle } from '../../hooks/usePageTitle.js'
import { t } from '../../i18n/index.js'
import { studio } from '../../content/site.js'
import logoBlack from '../../assets/brand/logo-black.png'
import '../../styles/client.css'

export default function ClientLoginPage() {
  usePageTitle(t('client.login.title'))
  const { status, user, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [form, setForm] = useState({ username: '', password: '' })
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  if (status === 'loading') return <Spinner fullScreen />
  if (user?.role === 'client') return <Navigate to="/client" replace />

  async function submit(e) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const me = await login(form.username, form.password)
      const target = location.state?.from?.startsWith('/client') ? location.state.from : '/client'
      navigate(me.role === 'admin' ? '/admin' : target, { replace: true })
    } catch (err) {
      setError(err.problem?.detail ?? err.message)
      setBusy(false)
    }
  }

  return (
    <div className="cl-login">
      <div className="cl-login__panel">
        <Link to="/" className="cl-brand cl-brand--large" aria-label="Frans Studios — home">
          <img className="cl-brand__logo" src={logoBlack} width={1402} height={368} alt="" />
        </Link>
        <h1 className="cl-login__title">{t('client.login.title')}</h1>
        <p className="cl-login__subtitle">{t('client.login.subtitle')}</p>

        <form className="cl-login__form" onSubmit={submit}>
          {error && (
            <p className="form-error" role="alert">
              {error}
            </p>
          )}
          <Field
            label={t('client.login.username')}
            name="username"
            autoComplete="username"
            autoCapitalize="none"
            spellCheck={false}
            required
            value={form.username}
            onChange={(e) => setForm({ ...form, username: e.target.value })}
          />
          <Field
            label={t('client.login.password')}
            name="password"
            type="password"
            autoComplete="current-password"
            required
            value={form.password}
            onChange={(e) => setForm({ ...form, password: e.target.value })}
          />
          <button type="submit" className="btn" disabled={busy}>
            {busy ? t('common.loading') : t('client.login.submit')}
          </button>
        </form>
        <p className="cl-login__help">
          {t('client.login.help')}{' '}
          <a href={studio.whatsappHref} target="_blank" rel="noopener noreferrer">
            WhatsApp
          </a>
        </p>
      </div>
    </div>
  )
}
