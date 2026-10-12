import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../services/api.js'

/**
 * Loads JSON from a GET endpoint, cancelling stale requests when the url/query changes.
 * Pass `null` as url to skip loading. While a new url/query is loading, the previous data
 * is kept (so tables don't flash) but `loading` is true from the very first render.
 * @returns {{ data: any, error: import('../services/api.js').ApiError|null, loading: boolean, reload: () => void, setData: Function }}
 */
export function useApi(url, query) {
  const queryKey = query ? JSON.stringify(query) : ''
  const key = url ? `${url}?${queryKey}` : null
  const [state, setState] = useState({ data: null, error: null, key: null })
  const [version, setVersion] = useState(0)
  const queryRef = useRef(query)
  queryRef.current = query

  useEffect(() => {
    if (!url) return
    const controller = new AbortController()
    const requestKey = `${url}?${queryKey}`
    // A reload of the same key refreshes quietly in the background (no spinner flash).
    api(url, { signal: controller.signal, query: queryRef.current })
      .then((data) => setState({ data, error: null, key: requestKey }))
      .catch((error) => {
        if (error.name !== 'AbortError') setState({ data: null, error, key: requestKey })
      })
    return () => controller.abort()
  }, [url, queryKey, version])

  const reload = useCallback(() => setVersion((v) => v + 1), [])
  const setData = useCallback((updater) => setState((s) => ({ ...s, data: typeof updater === 'function' ? updater(s.data) : updater })), [])

  return {
    data: state.data,
    error: state.key === key ? state.error : null,
    loading: key !== null && state.key !== key,
    reload,
    setData,
  }
}
