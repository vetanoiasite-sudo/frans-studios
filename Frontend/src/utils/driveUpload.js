import { api, ApiError, sendBytes } from '../services/api.js'

/**
 * Upload queue for sending files into the studio's Google Drive — into an album's sections, or into any
 * folder from the admin Drive manager. Each queue item has a `dest`: the album section, or the folder id.
 *
 * Each file uses Google's resumable protocol: the API opens a session (POST /uploads) and the file is sent
 * in chunks — straight from this browser to Google ("direct"), or through our API ("proxy"). How far Google
 * got is always asked through the API (POST /uploads/status), so a dropped connection, a pause or even a
 * page reload resumes from the last byte Google stored instead of starting over. If Google refuses the
 * browser's direct requests, the queue switches to proxy for the rest of the page.
 */

const CONCURRENCY = 3
const MAX_RETRIES = 8
const SAVED_PREFIX = 'fs-upload:'
const SAVED_MAX_AGE = 6 * 24 * 3600 * 1000 // Google keeps a session for 7 days
const IGNORED = /^(\..*|thumbs\.db|desktop\.ini)$/i

/** Files to upload from a file input or a drop: skips system files like .DS_Store and Thumbs.db. */
export function acceptable(file) {
  return file.size > 0 && !IGNORED.test(file.name)
}

/**
 * Which album section (Drive sub-folder) a file goes in. "auto": a dropped folder's own sub-folders are kept
 * ("Wedding/Photos/IMG_1.jpg" → Photos); loose files go to Videos or Photos by type.
 */
export function sectionFor(file, relativePath, choice) {
  if (choice !== 'auto') return choice
  const parts = (relativePath || '').split('/').filter(Boolean)
  if (parts.length >= 3) return parts[1]
  return file.type.startsWith('video/') ? 'Videos' : 'Photos'
}

class Expired extends Error {}

const sleep = (ms, signal) =>
  new Promise((resolve, reject) => {
    const t = setTimeout(resolve, ms)
    signal.addEventListener('abort', () => {
      clearTimeout(t)
      reject(Object.assign(new Error('Upload paused'), { name: 'AbortError' }))
    })
  })

/** Upload target: an album (dest = section name). */
export const albumTarget = (albumId) => ({
  id: `album-${albumId}`,
  base: `/api/admin/albums/${albumId}`,
  startBody: (item) => ({ section: item.dest }),
})

/** Upload target: the admin Drive manager (dest = Drive folder id). */
export const driveTarget = {
  id: 'drive',
  base: '/api/admin/drive',
  startBody: (item) => ({ parentId: item.dest }),
}

export class UploadQueue {
  /**
   * @param {{ id: string, base: string, startBody: (item) => object }} target  albumTarget(id) or driveTarget
   * @param {{ onChange: () => void, onFinished: (count: number) => void }} callbacks
   */
  constructor(target, { onChange, onFinished }) {
    this.target = target
    this.items = []
    this.running = 0
    this.directBlocked = false
    this.finishedSinceIdle = 0
    this.onChange = onChange
    this.onFinished = onFinished
    this.nextId = 1
  }

  /**
   * @param {{ file: File, dest: string, destLabel?: string }[]} entries  dest: album section or Drive folder id;
   *   destLabel: how to show it (defaults to dest)
   */
  add(entries) {
    const known = new Set(this.items.filter((i) => i.status !== 'done').map((i) => this.key(i.file, i.dest)))
    for (const { file, dest, destLabel } of entries) {
      if (known.has(this.key(file, dest))) continue // same file already queued
      this.items.push({ id: this.nextId++, file, name: file.name, size: file.size, dest, destLabel: destLabel ?? dest, sent: 0, status: 'queued', error: null, session: null, controller: null })
    }
    this.changed()
    this.pump()
  }

  pause(item) {
    if (item.status === 'queued') item.status = 'paused'
    item.controller?.abort()
    this.changed()
  }

  resume(item) {
    if (item.status === 'paused' || item.status === 'error') {
      item.status = 'queued'
      item.error = null
      this.changed()
      this.pump()
    }
  }

  remove(item) {
    item.controller?.abort()
    this.items = this.items.filter((i) => i !== item)
    this.changed()
  }

  pauseAll() {
    for (const item of this.items) if (item.status === 'queued' || item.status === 'uploading') this.pause(item)
  }

  resumeAll() {
    for (const item of this.items) if (item.status === 'paused' || item.status === 'error') this.resume(item)
  }

  clearDone() {
    this.items = this.items.filter((i) => i.status !== 'done')
    this.changed()
  }

  get busy() {
    return this.items.some((i) => i.status === 'uploading' || i.status === 'queued')
  }

  totals() {
    let size = 0
    let sent = 0
    const counts = { queued: 0, uploading: 0, paused: 0, error: 0, done: 0 }
    for (const i of this.items) {
      size += i.size
      sent += i.status === 'done' ? i.size : i.sent
      counts[i.status]++
    }
    return { size, sent, counts, total: this.items.length }
  }

  // ── internals ─────────────────────────────────────────────────────────────

  changed() {
    this.onChange?.()
  }

  pump() {
    while (this.running < CONCURRENCY) {
      const next = this.items.find((i) => i.status === 'queued')
      if (!next) break
      this.run(next)
    }
    if (this.running === 0 && !this.busy && this.finishedSinceIdle > 0) {
      const count = this.finishedSinceIdle
      this.finishedSinceIdle = 0
      this.onFinished?.(count)
    }
  }

  async run(item) {
    this.running++
    item.status = 'uploading'
    item.error = null
    item.controller = new AbortController()
    const { signal } = item.controller
    this.changed()

    try {
      let offset = 0
      item.session ??= this.loadSaved(item)
      if (item.session) {
        try {
          const st = await this.status(item)
          offset = st.complete ? item.size : st.received
        } catch (err) {
          if (!(err instanceof Expired)) throw err
          this.forget(item)
          item.session = null
        }
      }
      if (!item.session) {
        item.session = await this.open(item)
        offset = 0
      }
      item.sent = offset
      this.changed()

      let failures = 0
      while (offset < item.size) {
        if (signal.aborted) throw Object.assign(new Error('Upload paused'), { name: 'AbortError' })
        const end = Math.min(offset + item.session.chunkSize, item.size)
        try {
          const result = await this.sendChunk(item, offset, end, signal)
          offset = result.done ? item.size : result.next
          failures = 0
        } catch (err) {
          if (err.name === 'AbortError') throw err
          if (err instanceof Expired) {
            this.forget(item)
            item.session = await this.open(item)
            offset = 0
            continue
          }
          if (++failures > MAX_RETRIES) throw err
          await sleep(Math.min(30_000, 1000 * 2 ** (failures - 1)) + Math.random() * 500, signal)
          const st = await this.status(item) // where Google really is
          if (st.complete) {
            offset = item.size
            break
          }
          // The API can reach Google, Google has nothing, and the browser's own requests keep failing:
          // Google isn't accepting this browser's cross-origin uploads — send chunks through the API.
          if (err.status === 0 && navigator.onLine && st.received === 0 && failures >= 2 && item.session.mode === 'direct') this.directBlocked = true
          offset = st.received
        }
        item.sent = offset
        this.changed()
      }

      await api(`${this.target.base}/uploads/complete`, { method: 'POST', body: { uploadId: item.session.uploadId } })
      this.forget(item)
      item.status = 'done'
      item.sent = item.size
      this.finishedSinceIdle++
    } catch (err) {
      if (err.name === 'AbortError') item.status = 'paused'
      else {
        item.status = 'error'
        item.error = err instanceof ApiError ? err.message : err.status === 0 ? 'Connection lost. Press Retry when you’re back online.' : err.message
      }
    } finally {
      item.controller = null
      this.running--
      this.changed()
      this.pump()
    }
  }

  async open(item) {
    const session = await api(`${this.target.base}/uploads`, {
      method: 'POST',
      body: { fileName: item.name, size: item.size, mimeType: item.file.type || null, ...this.target.startBody(item) },
    })
    this.save(item, session)
    return session
  }

  async status(item) {
    try {
      return await api(`${this.target.base}/uploads/status`, { method: 'POST', body: { uploadId: item.session.uploadId } })
    } catch (err) {
      if (err instanceof ApiError && (err.status === 410 || err.status === 404)) throw new Expired(err.message)
      throw err
    }
  }

  async sendChunk(item, offset, end, signal) {
    const blob = item.file.slice(offset, end)
    const onProgress = (loaded) => {
      item.sent = offset + loaded
      this.changed()
    }

    if (item.session.mode === 'direct' && !this.directBlocked) {
      const r = await sendBytes(item.session.sessionUrl, blob, {
        headers: { 'Content-Range': `bytes ${offset}-${end - 1}/${item.size}` },
        onProgress,
        signal,
      })
      if (r.status === 200 || r.status === 201) return { done: true }
      if (r.status === 308) {
        // Google says which bytes it has ("bytes=0-N") when the header is readable; otherwise trust the chunk.
        const range = r.header('Range')
        return { next: range ? Number(range.split('-')[1]) + 1 : end }
      }
      if (r.status === 404 || r.status === 410) throw new Expired('Upload expired')
      throw Object.assign(new Error(`Google answered ${r.status}.`), { status: r.status })
    }

    const url = `${this.target.base}/uploads/chunk?uploadId=${encodeURIComponent(item.session.uploadId)}&offset=${offset}`
    const r = await sendBytes(url, blob, { sameOrigin: true, headers: { 'Content-Type': 'application/octet-stream' }, onProgress, signal })
    if (r.status === 200) return r.data.complete ? { done: true } : { next: r.data.received }
    if (r.status === 410) throw new Expired('Upload expired')
    throw Object.assign(new Error(r.data?.title ?? `Upload failed (${r.status}).`), { status: r.status })
  }

  // A session is remembered per file so closing the page doesn't lose a half-uploaded 4 GB film:
  // choosing the same file again (same target, destination, name, size, date) continues where it stopped.

  key(file, dest) {
    return `${SAVED_PREFIX}${this.target.id}:${dest}:${file.name}:${file.size}:${file.lastModified}`
  }

  save(item, session) {
    try {
      localStorage.setItem(this.key(item.file, item.dest), JSON.stringify({ ...session, savedAt: Date.now() }))
    } catch {
      /* storage unavailable: resuming after a reload just won't work */
    }
  }

  loadSaved(item) {
    try {
      const saved = JSON.parse(localStorage.getItem(this.key(item.file, item.dest)) ?? 'null')
      if (saved && Date.now() - saved.savedAt < SAVED_MAX_AGE) return saved
    } catch {
      /* ignore */
    }
    return null
  }

  forget(item) {
    try {
      localStorage.removeItem(this.key(item.file, item.dest))
    } catch {
      /* ignore */
    }
  }
}

/** Reads every file inside dropped folders (DataTransfer entries), keeping each file's relative path. */
export async function filesFromDrop(dataTransfer) {
  const entries = [...dataTransfer.items].map((i) => i.webkitGetAsEntry?.()).filter(Boolean)
  if (!entries.length) return [...dataTransfer.files].map((file) => ({ file, path: file.name }))

  const out = []
  async function walk(entry, path) {
    if (entry.isFile) {
      const file = await new Promise((resolve, reject) => entry.file(resolve, reject))
      out.push({ file, path: `${path}${file.name}` })
    } else if (entry.isDirectory) {
      const reader = entry.createReader()
      // readEntries returns at most ~100 entries per call; keep reading until it returns none.
      for (;;) {
        const batch = await new Promise((resolve, reject) => reader.readEntries(resolve, reject))
        if (!batch.length) break
        for (const child of batch) await walk(child, `${path}${entry.name}/`)
      }
    }
  }
  for (const entry of entries) await walk(entry, '')
  return out
}
