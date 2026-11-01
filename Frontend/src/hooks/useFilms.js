import { defaultFilms } from '../content/films.js'
import { useApi } from './useApi.js'

/**
 * Published films from Admin → Films, or the built-in ones when none are published (or the API fails),
 * so the home page and the Films page never show empty players. `data` is null while loading.
 */
export function useFilms() {
  const { data, error, loading } = useApi('/api/public/films')
  if (loading) return { data: null, loading }
  return { data: !error && data?.length ? data : defaultFilms, loading }
}
