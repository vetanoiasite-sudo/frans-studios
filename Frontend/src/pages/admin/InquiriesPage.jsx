import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { api } from '../../services/api.js'
import { useApi } from '../../hooks/useApi.js'
import { ErrorState, Spinner } from '../../components/ui/Spinner.jsx'
import { ErrorBanner, Notice, PageHeader, Pagination, StatusBadge } from '../../components/admin/AdminUi.jsx'
import { formatDate, formatDateTime } from '../../utils/format.js'

const statuses = ['new', 'contacted', 'booked', 'closed']
// Named after the website pages the forms are on.
const tabs = [
  { origin: 'packages', label: 'Package requests', hint: 'Sent from the Packages page (“Ready to Discover Our Packages?”).' },
  { origin: 'contact', label: 'Contact messages', hint: 'Sent from the Contact page (“Let’s Talk”).' },
]
const tone = { new: 'warn', contacted: 'neutral', booked: 'ok', closed: 'neutral' }

export default function InquiriesPage() {
  const [params, setParams] = useSearchParams()
  const origin = params.get('origin') === 'contact' ? 'contact' : 'packages'
  const status = params.get('status') ?? ''
  const page = Number(params.get('page') ?? 1)
  const { data, error, loading, reload, setData } = useApi('/api/admin/inquiries', { origin, status, page, pageSize: 25 })
  const [countsVersion, setCountsVersion] = useState(0)
  const { data: counts } = useApi('/api/admin/inquiries/counts', { v: countsVersion })
  const tab = tabs.find((t) => t.origin === origin)
  const [open, setOpen] = useState(null)
  // The request whose status is being saved, and how the last change went: { id, notice } or { id, error }.
  const [saving, setSaving] = useState(null)
  const [result, setResult] = useState(null)

  // Shown as changed right away; put back as it was if the server doesn't save it.
  async function setStatus(inquiry, next) {
    const previous = inquiry.status
    const setLocal = (value) => setData((d) => ({ ...d, items: d.items.map((i) => (i.id === inquiry.id ? { ...i, status: value } : i)) }))
    setLocal(next)
    setSaving(inquiry.id)
    setResult(null)
    try {
      await api(`/api/admin/inquiries/${inquiry.id}`, { method: 'PUT', body: { status: next } })
      setResult({ id: inquiry.id, notice: `Saved: marked as ${next}.` })
      setCountsVersion((v) => v + 1)
    } catch (err) {
      setLocal(previous)
      setResult({ id: inquiry.id, error: err })
    } finally {
      setSaving(null)
    }
  }

  return (
    <>
      <PageHeader title="Requests" subtitle="What visitors send from the website’s Packages and Contact pages." />
      <div className="adm-tabs adm-requests-tabs" role="tablist" aria-label="Request type">
        {tabs.map((t) => {
          const isNew = counts ? counts[`${t.origin}New`] : 0
          return (
            <button key={t.origin} type="button" role="tab" aria-selected={origin === t.origin} onClick={() => setParams({ origin: t.origin })}>
              {t.label}
              {isNew > 0 && <span className="adm-nav__badge">{isNew}</span>}
            </button>
          )
        })}
      </div>
      <p className="adm-muted adm-small">
        {tab.hint} {origin === 'packages' && <>Package prices and details shown on that page are set in <Link to="/admin/packages">Package plans</Link>.</>}
      </p>
      <div className="adm-filters">
        <select className="adm-input" aria-label="Status" value={status} onChange={(e) => setParams({ origin, status: e.target.value })}>
          <option value="">All</option>
          {statuses.map((s) => (
            <option key={s} value={s}>
              {s[0].toUpperCase() + s.slice(1)}
            </option>
          ))}
        </select>
      </div>
      {loading && !data && <Spinner />}
      {error && <ErrorState error={error} onRetry={reload} />}
      {data && (
        <>
          <ul className="adm-inquiries" role="list">
            {data.items.length === 0 && <li className="adm-muted">{origin === 'packages' ? 'No package requests yet.' : 'No contact messages yet.'}</li>}
            {data.items.map((i) => (
              <li key={i.id} className="adm-card adm-inquiry">
                <button type="button" className="adm-inquiry__head" aria-expanded={open === i.id} onClick={() => setOpen(open === i.id ? null : i.id)}>
                  <strong>{i.name}</strong>
                  <span className="adm-muted adm-small adm-ellipsis">
                    {i.origin === 'contact'
                      ? i.message?.slice(0, 120) || '—'
                      : [i.eventType, i.eventDate && formatDate(i.eventDate), i.venue].filter(Boolean).join(' · ') || '—'}
                  </span>
                  <span className="adm-muted adm-small">{formatDateTime(i.createdAt)}</span>
                  <StatusBadge tone={tone[i.status]}>{i.status}</StatusBadge>
                </button>
                {open === i.id && (
                  <div className="adm-inquiry__body">
                    <dl className="adm-credentials">
                      <dt>Mobile</dt>
                      <dd>
                        <a href={`tel:${i.mobile}`}>{i.mobile}</a> ·{' '}
                        <a href={`https://wa.me/${i.mobile.replace(/[^\d]/g, '')}`} target="_blank" rel="noopener noreferrer">
                          WhatsApp
                        </a>
                      </dd>
                      {i.email && (
                        <>
                          <dt>Email</dt>
                          <dd>
                            <a href={`mailto:${i.email}`}>{i.email}</a>
                          </dd>
                        </>
                      )}
                      {i.origin === 'packages' && (
                        <>
                          <dt>Coverage</dt>
                          <dd>{i.coverage ?? '—'}</dd>
                          <dt>Heard via</dt>
                          <dd>{i.source ?? '—'}</dd>
                        </>
                      )}
                    </dl>
                    {i.message && <p className="adm-message">{i.message}</p>}
                    <div className="adm-form__actions">
                      {statuses.map((s) => (
                        <button
                          key={s}
                          type="button"
                          className={s === i.status ? 'adm-btn adm-btn--small adm-btn--primary' : 'adm-btn adm-btn--small'}
                          disabled={saving === i.id}
                          onClick={() => setStatus(i, s)}
                        >
                          {s}
                        </button>
                      ))}
                    </div>
                    {saving === i.id && <p className="adm-muted adm-small">Saving…</p>}
                    <Notice>{result?.id === i.id && result.notice}</Notice>
                    {result?.id === i.id && <ErrorBanner error={result.error} />}
                  </div>
                )}
              </li>
            ))}
          </ul>
          <Pagination page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => setParams({ origin, status, page: String(p) })} />
        </>
      )}
    </>
  )
}
