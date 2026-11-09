import { CreditCard, XCircle } from 'lucide-react'
import { StatusBadge } from '../../components/StatusBadge'
import type { HoldStatus, MyHold } from '../../types/api'
import { effectiveStatus, formatDateTime } from '../../utils/time'
import { Countdown } from './Countdown'

interface Props {
  hold: MyHold
  now: number
  disabled: boolean
  onPay: (h: MyHold) => void
  onCancel: (h: MyHold) => void
}

const NOTES: Partial<Record<HoldStatus, string>> = {
  CONFIRMED: 'Admission confirmed. Congratulations!',
  EXPIRED: 'Your reservation has expired. Please apply again if seats are available.',
  CANCELLED: 'This reservation was cancelled.',
}

export function HoldCard({ hold, now, disabled, onPay, onCancel }: Props) {
  const status = effectiveStatus(hold.status, hold.expiresAt, now)
  const remainingMs = Date.parse(hold.expiresAt) - now

  return (
    <li className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-base font-semibold text-slate-900">{hold.className}</h3>
          <p className="mt-0.5 break-all font-mono text-xs text-slate-600">Hold ID: {hold.holdId}</p>
        </div>
        <StatusBadge status={status} />
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-x-6 gap-y-2 text-sm text-slate-700">
        <p>
          {status === 'HELD' ? 'Expires' : 'Expiry time'}: <time dateTime={hold.expiresAt}>{formatDateTime(hold.expiresAt)}</time>
        </p>
        {status === 'HELD' && <Countdown remainingMs={remainingMs} />}
      </div>

      {NOTES[status] && (
        <p className={`mt-3 rounded-lg px-3 py-2 text-sm ${status === 'CONFIRMED' ? 'bg-emerald-50 text-emerald-900' : 'bg-slate-100 text-slate-800'}`}>{NOTES[status]}</p>
      )}

      {(status === 'HELD' || status === 'EXPIRED') && (
        <div className="mt-4 flex flex-col gap-2 sm:flex-row">
          <button
            type="button"
            disabled={disabled || status !== 'HELD'}
            onClick={() => onPay(hold)}
            className="inline-flex items-center justify-center gap-2 rounded-lg bg-emerald-700 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-800 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-700 disabled:cursor-not-allowed disabled:bg-slate-300 disabled:text-slate-700"
          >
            <CreditCard className="h-4 w-4" aria-hidden="true" />
            Pay Admission Fee
          </button>
          {status === 'HELD' && (
            <button
              type="button"
              disabled={disabled}
              onClick={() => onCancel(hold)}
              className="inline-flex items-center justify-center gap-2 rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-800 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
            >
              <XCircle className="h-4 w-4" aria-hidden="true" />
              Cancel reservation
            </button>
          )}
        </div>
      )}
    </li>
  )
}
