import { useEffect, useState } from 'react'
import { NavLink, Outlet, ScrollRestoration, useLocation, useNavigate } from 'react-router'
import { RequireRole, useAuth } from '../context/AuthContext.jsx'
import { useApi } from '../hooks/useApi.js'
import '../styles/admin.css'

const nav = [
  { to: '/admin', label: 'Overview', end: true },
  { group: 'Clients' },
  { to: '/admin/clients', label: 'Clients' },
  { to: '/admin/albums', label: 'Albums' },
  { to: '/admin/drive', label: 'Drive' },
  { group: 'Website' },
  { to: '/admin/portfolio', label: 'Portfolio' },
  { to: '/admin/films', label: 'Films' },
  { to: '/admin/packages', label: 'Package plans' },
  { to: '/admin/page-covers', label: 'Page covers' },
  { to: '/admin/inquiries', label: 'Requests', badge: 'requests' },
  { group: 'Account' },
  { to: '/admin/google-drive', label: 'Drive settings' },
  { to: '/admin/settings', label: 'Settings & activity' },
]

function AdminShell() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [open, setOpen] = useState(false)
  // New website requests, shown next to "Requests"; refreshed on every page change.
  const { data: counts } = useApi('/api/admin/inquiries/counts', { v: location.pathname })
  const badges = { requests: counts ? counts.packagesNew + counts.contactNew : 0 }

  useEffect(() => setOpen(false), [location.pathname])
  useEffect(() => {
    document.title = 'Admin — Vetanoia'
  }, [location.pathname])

  async function signOut() {
    await logout()
    navigate('/admin/login', { replace: true })
  }

  return (
    <div className="adm">
      <header className="adm-top">
        <button type="button" className="adm-top__menu" aria-expanded={open} aria-controls="adm-nav" onClick={() => setOpen((o) => !o)}>
          <span className="visually-hidden">Menu</span>☰
        </button>
        <span className="adm-top__brand">Vetanoia · Admin</span>
        <div className="adm-top__user">
          <a href="/" target="_blank" rel="noopener noreferrer" className="adm-top__link">
            View site ↗
          </a>
          <span className="adm-top__name">{user?.username}</span>
          <button type="button" className="adm-btn adm-btn--quiet" onClick={signOut}>
            Sign out
          </button>
        </div>
      </header>
      <nav id="adm-nav" className="adm-nav" data-open={open} aria-label="Admin">
        {nav.map((item, i) =>
          item.group ? (
            <p key={i} className="adm-nav__group">
              {item.group}
            </p>
          ) : (
            <NavLink key={item.to} to={item.to} end={item.end} className="adm-nav__link">
              {item.label}
              {item.badge && badges[item.badge] > 0 && (
                <span className="adm-nav__badge" aria-label={`${badges[item.badge]} new`}>
                  {badges[item.badge]}
                </span>
              )}
            </NavLink>
          ),
        )}
      </nav>
      <main className="adm-main">
        <Outlet />
      </main>
      <ScrollRestoration />
    </div>
  )
}

export default function AdminLayout() {
  return (
    <RequireRole role="admin" loginPath="/admin/login">
      <AdminShell />
    </RequireRole>
  )
}
