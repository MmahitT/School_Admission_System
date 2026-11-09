import { useCallback, useEffect, useRef, useState } from 'react'

export interface PolledResource<T> {
  data: T | null
  error: unknown
  loading: boolean
  refresh: () => Promise<void>
}

/**
 * Fetches immediately, then every `intervalMs` while the tab is visible (and again when it becomes
 * visible). Stale responses are dropped; timers and listeners are removed on unmount.
 * Changing `resetKey` (e.g. the student id) clears the data and refetches.
 */
export function usePolledResource<T>(
  fetcher: () => Promise<T>,
  intervalMs: number,
  resetKey: unknown = null,
  enabled = true,
): PolledResource<T> {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [loading, setLoading] = useState(true)
  const fetcherRef = useRef(fetcher)
  const requestId = useRef(0)
  const mounted = useRef(false)

  useEffect(() => {
    fetcherRef.current = fetcher
  })

  const refresh = useCallback(async () => {
    const id = ++requestId.current
    try {
      const result = await fetcherRef.current()
      if (mounted.current && id === requestId.current) {
        setData(result)
        setError(null)
      }
    } catch (e) {
      if (mounted.current && id === requestId.current) setError(e)
    } finally {
      if (mounted.current && id === requestId.current) setLoading(false)
    }
  }, [])

  useEffect(() => {
    mounted.current = true
    if (!enabled) return
    setData(null)
    setError(null)
    setLoading(true)
    void refresh()

    const tick = () => {
      if (!document.hidden) void refresh()
    }
    const timer = setInterval(tick, intervalMs)
    document.addEventListener('visibilitychange', tick)
    return () => {
      mounted.current = false
      clearInterval(timer)
      document.removeEventListener('visibilitychange', tick)
    }
  }, [enabled, intervalMs, refresh, resetKey])

  return { data, error, loading, refresh }
}
