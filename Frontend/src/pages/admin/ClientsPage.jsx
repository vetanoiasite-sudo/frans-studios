import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useApi } from '../../hooks/useApi.js'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { PageHeader, Pagination, StatusBadge } from '../../components/admin/AdminUi.jsx'
import { formatDate, formatDateTime } from '../../utils/format.js'

export default function ClientsPage() {
  const [params, setParams] = useSearchParams()
  const page = Number(params.get('page') ?? 1)
  const status = params.get('status') ?? ''
  const search = params.get('search') ?? ''
  const [draft, setDraft] = useState(search)

  // Debounce typing into the URL (which drives the request).
  useEffect(() => {
    const id = setTimeout(() => {
      if (draft !== search) setParams((p) => ({ ...Object.fromEntries(p), search: draft, page: '1' }))
    }, 300)
    return () => clearTimeout(id)
  }, [draft, search, setParams])

  const { data, error, loading, reload } = useApi('/api/admin/clients', { search, status, page, pageSize: 25 })
  const update = (patch) => setParams((p) => ({ ...Object.fromEntries(p), ...patch }))

  return (
    <>
      <PageHeader
        title="Clients"
        subtitle="Couples and clients who can sign in to their private galleries."
        actions={
          <Link to="/admin/clients/new" className="adm-btn adm-btn--primary">
            + New client
          </Link>
        }
      />

      <div className="adm-filters">
        <input type="search" className="adm-input" placeholder="Search name, username, email, phone…" aria-label="Search clients" value={draft} onChange={(e) => setDraft(e.target.value)} />
        <select className="adm-input" aria-label="Status" value={status} onChange={(e) => update({ status: e.target.value, page: '1' })}>
          <option value="">All statuses</option>
          <option value="active">Active</option>
          <option value="disabled">Disabled</option>
        </select>
      </div>

      {loading && !data && <Spinner />}
      {error && <ErrorState error={error} onRetry={reload} />}
      {data && (
        <>
          <div className="adm-table-wrap">
            <table className="adm-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Username</th>
                  <th>Contact</th>
                  <th>Albums</th>
                  <th>Last login</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {data.items.length === 0 && (
                  <tr>
                    <td colSpan={6} className="adm-muted">
                      No clients found.
                    </td>
                  </tr>
                )}
                {data.items.map((c) => (
                  <tr key={c.id}>
                    <td>
                      <Link to={`/admin/clients/${c.id}`} className="adm-row-link">
                        {c.fullName}
                      </Link>
                      <div className="adm-muted adm-small">Since {formatDate(c.createdAt)}</div>
                    </td>
                    <td>
                      <code>{c.username}</code>
                    </td>
                    <td className="adm-small">{[c.email, c.phone].filter(Boolean).join(' · ') || '—'}</td>
                    <td>{c.albumCount}</td>
                    <td className="adm-small">{c.lastLoginAt ? formatDateTime(c.lastLoginAt) : 'Never'}</td>
                    <td>{c.isActive ? <StatusBadge tone="ok">Active</StatusBadge> : <StatusBadge tone="danger">Disabled</StatusBadge>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => update({ page: String(p) })} />
        </>
      )}
    </>
  )
}
