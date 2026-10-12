import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { useAuth } from '../../context/AuthContext.jsx'
import { Field } from '../../components/ui/Field.jsx'
import { Spinner } from '../../components/ui/Spinner.jsx'
import '../../styles/admin.css'

export default function AdminLoginPage() {
  const { status, user, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [form, setForm] = useState({ username: '', password: '' })
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  if (status === 'loading') return <Spinner fullScreen />
  if (user?.role === 'admin') return <Navigate to="/admin" replace />

  async function submit(e) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const me = await login(form.username, form.password)
      if (me.role !== 'admin') {
        navigate('/client', { replace: true })
        return
      }
      const from = location.state?.from
      navigate(from?.startsWith('/admin') ? from : '/admin', { replace: true })
    } catch (err) {
      setError(err.problem?.detail ?? err.message)
      setBusy(false)
    }
  }

  return (
    <div className="adm-login">
      <form className="adm-login__card" onSubmit={submit}>
        <h1>Studio admin</h1>
        <p className="adm-muted">Frans Studios</p>
        {error && (
          <p className="form-error" role="alert">
            {error}
          </p>
        )}
        <Field label="Username" autoComplete="username" autoCapitalize="none" required value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} />
        <Field label="Password" type="password" autoComplete="current-password" required value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} />
        <button type="submit" className="adm-btn adm-btn--primary" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </div>
  )
}
