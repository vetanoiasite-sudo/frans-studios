import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { Navigate, useLocation } from 'react-router'
import { api, ApiError, onUnauthorized } from '../services/api.js'
import { Spinner } from '../components/ui/Spinner.jsx'

const AuthContext = createContext(null)

/**
 * Knows who is signed in by asking the API (GET /api/auth/me). The role here only drives
 * navigation; every permission is enforced again by the backend on each request.
 */
export function AuthProvider({ children }) {
  const [state, setState] = useState({ status: 'loading', user: null })

  const refresh = useCallback(async () => {
    try {
      const user = await api('/api/auth/me')
      setState({ status: 'ready', user })
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) setState({ status: 'ready', user: null })
      else setState({ status: 'error', user: null })
    }
  }, [])

  useEffect(() => {
    refresh()
    return onUnauthorized(() => setState({ status: 'ready', user: null }))
  }, [refresh])

  const login = useCallback(async (username, password) => {
    const user = await api('/api/auth/login', { method: 'POST', body: { username, password } })
    setState({ status: 'ready', user })
    return user
  }, [])

  const logout = useCallback(async () => {
    try {
      await api('/api/auth/logout', { method: 'POST' })
    } finally {
      setState({ status: 'ready', user: null })
    }
  }, [])

  const value = useMemo(() => ({ ...state, login, logout, refresh }), [state, login, logout, refresh])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}

/** Route guard for /admin and /client areas (UX only — the API enforces the same rule). */
export function RequireRole({ role, loginPath, children }) {
  const { status, user } = useAuth()
  const location = useLocation()

  if (status === 'loading') return <Spinner fullScreen />
  if (!user) return <Navigate to={loginPath} replace state={{ from: location.pathname + location.search }} />
  if (user.role !== role) return <Navigate to={user.role === 'admin' ? '/admin' : '/client'} replace />
  return children
}
