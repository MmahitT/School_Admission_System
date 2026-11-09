import { Timer } from 'lucide-react'
import { formatRemaining } from '../../utils/time'

/** Presentational: the parent supplies remaining ms derived from the server's expiresAt. */
export function Countdown({ remainingMs }: { remainingMs: number }) {
  const urgent = remainingMs <= 60_000
  return (
    <span
      role="timer"
      aria-label="Time remaining"
      className={`inline-flex items-center gap-1.5 font-mono text-lg font-semibold ${urgent ? 'text-red-700' : 'text-slate-900'}`}
    >
      <Timer className="h-4 w-4" aria-hidden="true" />
      {formatRemaining(remainingMs)}
    </span>
  )
}
