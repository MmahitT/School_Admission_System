import type { HoldStatus } from '../types/api'

/** Whole seconds, rounded down, so the display never exceeds the real remaining time. */
export function formatRemaining(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000))
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'medium' })
}

/** A HELD hold past its expiry is shown as EXPIRED immediately; the next poll confirms it with the server. */
export function effectiveStatus(status: HoldStatus, expiresAt: string, nowMs: number): HoldStatus {
  return status === 'HELD' && Date.parse(expiresAt) <= nowMs ? 'EXPIRED' : status
}
