import { useState } from 'react'
import { useApi } from '../../hooks/useApi.js'
import { Field } from '../ui/Field.jsx'

/** Client dropdown with a filter box (searches the first 100 matching clients). */
export function ClientSelect({ value, onChange, error, label = 'Client' }) {
  const [search, setSearch] = useState('')
  const { data } = useApi('/api/admin/clients', { search, pageSize: 100 })
  const options = data?.items ?? []
  return (
    <div className="adm-client-select">
      <Field label={label} as="select" required value={value} onChange={(e) => onChange(e.target.value)} error={error}>
        <option value="">Choose a client…</option>
        {options.map((c) => (
          <option key={c.id} value={c.id}>
            {c.fullName} ({c.username})
          </option>
        ))}
      </Field>
      <input type="search" className="adm-input adm-input--small" placeholder="Filter clients…" aria-label="Filter clients" value={search} onChange={(e) => setSearch(e.target.value)} />
    </div>
  )
}
