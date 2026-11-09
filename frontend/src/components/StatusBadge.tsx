import { Ban, CheckCircle2, Clock, Hourglass } from 'lucide-react'
import type { HoldStatus } from '../types/api'

const CONFIG = {
  HELD: { label: 'Held', hint: 'Reservation active', Icon: Hourglass, style: 'bg-amber-100 text-amber-900 ring-amber-300' },
  CONFIRMED: { label: 'Confirmed', hint: 'Admission confirmed', Icon: CheckCircle2, style: 'bg-emerald-100 text-emerald-900 ring-emerald-300' },
  EXPIRED: { label: 'Expired', hint: 'Reservation expired', Icon: Clock, style: 'bg-slate-200 text-slate-800 ring-slate-300' },
  CANCELLED: { label: 'Cancelled', hint: 'Reservation cancelled', Icon: Ban, style: 'bg-red-100 text-red-900 ring-red-300' },
} as const

/** Status is conveyed by text and icon as well as colour. */
export function StatusBadge({ status }: { status: HoldStatus }) {
  const { label, hint, Icon, style } = CONFIG[status]
  return (
    <span title={hint} data-status={status} className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-semibold ring-1 ring-inset ${style}`}>
      <Icon className="h-3.5 w-3.5" aria-hidden="true" />
      <span>{label.toUpperCase()}</span>
    </span>
  )
}
