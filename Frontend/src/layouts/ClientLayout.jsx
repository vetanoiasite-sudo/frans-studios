import { Link, NavLink, Outlet, ScrollRestoration, useNavigate } from 'react-router'
import { RequireRole, useAuth } from '../context/AuthContext.jsx'
import { useApi } from '../hooks/useApi.js'
import logoBlack from '../assets/brand/logo-black.png'
import { t } from '../i18n/index.js'
import '../styles/client.css'

function ClientShell() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const { data: me } = useApi('/api/client/me')

  async function signOut() {
    await logout()
    navigate('/client/login', { replace: true })
  }

  return (
    <div className="cl-shell">
      <header className="cl-bar">
        <Link to="/client" className="cl-brand" aria-label="Frans Studios — your galleries">
          <img className="cl-brand__logo" src={logoBlack} width={1402} height={368} alt="" />
        </Link>
        <div className="cl-bar__user">
          {me?.albumCount > 1 && (
            <NavLink to="/client" end className="cl-bar__link">
              My galleries <span className="cl-bar__count">{me.albumCount}</span>
            </NavLink>
          )}
          <span className="cl-bar__name">{user?.displayName}</span>
          <button type="button" className="cl-bar__logout" onClick={signOut}>
            {t('client.logout')}
          </button>
        </div>
      </header>
      <main className="cl-main">
        <Outlet />
      </main>
      <ScrollRestoration />
    </div>
  )
}

export default function ClientLayout() {
  return (
    <RequireRole role="client" loginPath="/client/login">
      <ClientShell />
    </RequireRole>
  )
}
