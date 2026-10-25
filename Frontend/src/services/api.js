// Thin fetch wrapper for the same-origin ASP.NET Core API.
// - Auth is an HttpOnly cookie the browser sends automatically; nothing sensitive is stored in JS.
// - Unsafe requests echo the XSRF-TOKEN cookie in the X-XSRF-TOKEN header (CSRF protection).
// - Errors arrive as RFC 7807 ProblemDetails and are thrown as ApiError.

export class ApiError extends Error {
  constructor(status, problem) {
    super(problem?.title || `Request failed (${status})`)
    this.status = status
    this.problem = problem
    /** Field errors from ValidationProblemDetails: { fieldName: [messages] } (keys camelCased). */
    this.fieldErrors = normaliseErrors(problem?.errors)
  }
}

function normaliseErrors(errors) {
  if (!errors) return {}
  const out = {}
  for (const [key, messages] of Object.entries(errors)) {
    const field = key.replace(/^\$\./, '').replace(/^[A-Z]/, (c) => c.toLowerCase())
    out[field] = messages
  }
  return out
}

function readCookie(name) {
  const match = document.cookie.split('; ').find((c) => c.startsWith(name + '='))
  return match ? decodeURIComponent(match.slice(name.length + 1)) : null
}

let csrfRequest = null
/** @param {boolean} [force] fetch a new token even if one is cached (e.g. after it went stale). */
export function ensureCsrf(force = false) {
  if (!force && readCookie('XSRF-TOKEN')) return Promise.resolve()
  csrfRequest ??= fetch('/api/auth/csrf', { credentials: 'same-origin' }).finally(() => {
    csrfRequest = null
  })
  return csrfRequest
}

// The token is bound to the signed-in user, so it goes stale when the session expires or the user
// changes (the cookie outlives both). The server then answers 400 with this title.
const CSRF_REJECTED = 'Invalid or missing CSRF token.'

async function isCsrfRejection(response) {
  if (response.status !== 400) return false
  try {
    return (await response.clone().json())?.title === CSRF_REJECTED
  } catch {
    return false
  }
}

const listeners = new Set()
/** Called with no arguments whenever the API says the session is gone (401). */
export function onUnauthorized(fn) {
  listeners.add(fn)
  return () => listeners.delete(fn)
}

/**
 * @param {string} path  e.g. "/api/client/albums"
 * @param {{method?: string, body?: any, form?: FormData, signal?: AbortSignal, query?: Record<string, any>}} [options]
 */
export async function api(path, { method = 'GET', body, form, signal, query } = {}) {
  const url = query ? `${path}?${toQuery(query)}` : path
  const headers = { Accept: 'application/json' }
  const unsafe = method !== 'GET' && method !== 'HEAD'

  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const send = async (refreshCsrf) => {
    if (unsafe) {
      await ensureCsrf(refreshCsrf)
      headers['X-XSRF-TOKEN'] = readCookie('XSRF-TOKEN') ?? ''
    }
    return fetch(url, {
      method,
      headers,
      body: form ?? (body !== undefined ? JSON.stringify(body) : undefined),
      credentials: 'same-origin',
      signal,
    })
  }

  let response
  try {
    response = await send(false)
  } catch (err) {
    if (err.name === 'AbortError') throw err
    // No response at all (offline, server down): say so in plain words rather than the browser's "Failed to fetch".
    throw new ApiError(0, { title: 'Couldn’t reach the server. Check your internet connection and try again.' })
  }
  // Stale token: get a fresh one and retry once.
  if (unsafe && (await isCsrfRejection(response))) response = await send(true)

  if (response.status === 401 && !path.startsWith('/api/auth/')) listeners.forEach((fn) => fn())

  if (!response.ok) {
    let problem = null
    try {
      problem = await response.json()
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(response.status, problem)
  }

  if (response.status === 204 || response.status === 202) return null
  const type = response.headers.get('Content-Type') ?? ''
  return type.includes('json') ? response.json() : response.text()
}

export function toQuery(params) {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  return search.toString()
}

/**
 * Upload with progress (fetch can't report upload progress). Returns parsed JSON.
 * @param {(fraction: number) => void} [onProgress]
 */
export async function upload(path, form, onProgress) {
  try {
    return await uploadOnce(path, form, onProgress, false)
  } catch (err) {
    // Stale token: get a fresh one and retry once.
    if (err instanceof ApiError && err.status === 400 && err.problem?.title === CSRF_REJECTED) {
      return uploadOnce(path, form, onProgress, true)
    }
    throw err
  }
}

async function uploadOnce(path, form, onProgress, refreshCsrf) {
  await ensureCsrf(refreshCsrf)
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', path)
    xhr.withCredentials = true
    xhr.setRequestHeader('X-XSRF-TOKEN', readCookie('XSRF-TOKEN') ?? '')
    xhr.setRequestHeader('Accept', 'application/json')
    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress?.(e.loaded / e.total)
    xhr.onload = () => {
      let data = null
      try {
        data = xhr.responseText ? JSON.parse(xhr.responseText) : null
      } catch {
        /* ignore */
      }
      if (xhr.status >= 200 && xhr.status < 300) resolve(data)
      else reject(new ApiError(xhr.status, data))
    }
    xhr.onerror = () => reject(new ApiError(0, { title: 'Network error. Check your connection and try again.' }))
    xhr.send(form)
  })
}

/**
 * Public API responses are cached by the browser for up to 60 s. After the admin changes site content,
 * re-fetch those URLs past the cache so this browser shows the change immediately (visitors still
 * see it within a minute).
 */
export function refreshPublicCache(...paths) {
  return Promise.all(paths.map((p) => fetch(p, { cache: 'reload', credentials: 'same-origin' }).catch(() => {})))
}

/**
 * Sends raw bytes (one chunk of a file) with upload progress, and resolves with the response status,
 * parsed JSON (if any) and a header reader. Never rejects on HTTP errors — callers decide what a status
 * means (Google's resumable protocol uses 308 for "keep going"). Rejects with status 0 on network/CORS
 * failure or "aborted" when the signal fires.
 * `sameOrigin` adds the session cookie and CSRF header (our API); off for Google's upload URLs.
 */
export async function sendBytes(url, body, { method = 'PUT', headers = {}, sameOrigin = false, onProgress, signal } = {}) {
  if (sameOrigin) await ensureCsrf()
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open(method, url)
    xhr.withCredentials = sameOrigin
    if (sameOrigin) xhr.setRequestHeader('X-XSRF-TOKEN', readCookie('XSRF-TOKEN') ?? '')
    for (const [name, value] of Object.entries(headers)) xhr.setRequestHeader(name, value)
    xhr.upload.onprogress = (e) => onProgress?.(e.loaded)
    xhr.onload = () => {
      let data = null
      try {
        data = xhr.responseText ? JSON.parse(xhr.responseText) : null
      } catch {
        /* not JSON */
      }
      resolve({ status: xhr.status, data, header: (name) => xhr.getResponseHeader(name) })
    }
    xhr.onerror = () => reject(Object.assign(new Error('Network error'), { status: 0 }))
    xhr.onabort = () => reject(Object.assign(new Error('Upload paused'), { name: 'AbortError' }))
    signal?.addEventListener('abort', () => xhr.abort(), { once: true })
    xhr.send(body)
  })
}
