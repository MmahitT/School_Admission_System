import { useCallback, useEffect, useRef, useState } from 'react'

export interface PolledResource<T> {
  data: T | null
  error: unknown
  loading: boolean
  refresh: () => Promise<void>
}

interface State<T> {
  key: unknown
  data: T | null
  error: unknown
  loading: boolean
}

/**
 * Fetches immediately, then every `intervalMs` while the tab is visible (and again when it becomes
 * visible). Stale responses are dropped; timers and listeners are removed on unmount.
 * Changing `resetKey` (e.g. the student id) discards the old data and refetches.
 */
export function usePolledResource<T>(
  fetcher: () => Promise<T>,
  intervalMs: number,
  resetKey: unknown = null,
  enabled = true,
): PolledResource<T> {
  const [state, setState] = useState<State<T>>({ key: resetKey, data: null, error: null, loading: true })
  const fetcherRef = useRef(fetcher)
  const keyRef = useRef(resetKey)
  const requestId = useRef(0)
  const mounted = useRef(false)

  useEffect(() => {
    fetcherRef.current = fetcher
    keyRef.current = resetKey
  })

  const refresh = useCallback(async () => {
    const id = ++requestId.current
    const key = keyRef.current
    try {
      const result = await fetcherRef.current()
      if (mounted.current && id === requestId.current) setState({ key, data: result, error: null, loading: false })
    } catch (error) {
      // Keep showing the last good data for this key; only surface the error.
      if (mounted.current && id === requestId.current) {
        setState((prev) => ({ key, data: prev.key === key ? prev.data : null, error, loading: false }))
      }
    }
  }, [])

  useEffect(() => {
    mounted.current = true
    if (!enabled) return
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

  // Derived during render: data from a previous key is never shown for the new one.
  const current = state.key === resetKey ? state : { data: null, error: null, loading: true }
  return { data: current.data, error: current.error, loading: current.loading, refresh }
}
