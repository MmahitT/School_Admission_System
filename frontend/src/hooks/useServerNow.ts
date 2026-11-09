import { useEffect, useState } from 'react'
import { serverNow } from '../utils/serverClock'

/** Server-corrected "now" in epoch ms, re-rendered every `intervalMs`. */
export function useServerNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => serverNow())
  useEffect(() => {
    const timer = setInterval(() => setNow(serverNow()), intervalMs)
    return () => clearInterval(timer)
  }, [intervalMs])
  return now
}
