import { Users } from 'lucide-react'
import { SeatMeter } from '../../components/SeatMeter'
import { StatusBadge } from '../../components/StatusBadge'
import type { AdmissionClass, HoldStatus } from '../../types/api'
import { seatState, type SeatState } from '../../utils/seats'

const STATE_BADGE: Record<SeatState, { label: string; style: string }> = {
  available: { label: 'Available', style: 'bg-emerald-100 text-emerald-900 ring-emerald-300' },
  limited: { label: 'Limited seats', style: 'bg-amber-100 text-amber-900 ring-amber-300' },
  full: { label: 'Fully booked', style: 'bg-red-100 text-red-900 ring-red-300' },
}

interface Props {
  admissionClass: AdmissionClass
  /** This student's non-expired application to this class, if any. */
  applicationStatus?: HoldStatus
  /** The student already holds a seat in some class (only one active hold allowed). */
  hasOtherActiveHold: boolean
  onApply: (c: AdmissionClass) => void
}

function buttonState(c: AdmissionClass, applicationStatus: HoldStatus | undefined, hasOtherActiveHold: boolean) {
  if (applicationStatus === 'HELD') return { label: 'Reservation active', disabled: true }
  if (applicationStatus === 'CONFIRMED') return { label: 'Admission confirmed', disabled: true }
  if (applicationStatus === 'CANCELLED') return { label: 'Application cancelled', disabled: true }
  if (c.availableSeats <= 0) return { label: 'Fully booked', disabled: true }
  if (hasOtherActiveHold) return { label: 'Finish your current reservation', disabled: true }
  return { label: 'Apply', disabled: false }
}

export function ClassCard({ admissionClass: c, applicationStatus, hasOtherActiveHold, onApply }: Props) {
  const state = seatState(c.availableSeats, c.totalSeats)
  const badge = STATE_BADGE[state]
  const button = buttonState(c, applicationStatus, hasOtherActiveHold)

  return (
    <article className="flex flex-col gap-4 rounded-xl border border-slate-200 bg-white p-5 shadow-sm" aria-label={c.name}>
      <header className="flex items-start justify-between gap-3">
        <h3 className="text-base font-semibold text-slate-900">{c.name}</h3>
        <span className={`shrink-0 rounded-full px-2.5 py-0.5 text-xs font-semibold ring-1 ring-inset ${badge.style}`}>{badge.label}</span>
      </header>

      <div>
        <p className="flex items-baseline gap-1.5 text-slate-900">
          <Users className="h-4 w-4 self-center text-slate-500" aria-hidden="true" />
          <span className="text-2xl font-bold">{c.availableSeats}</span>
          <span className="text-sm text-slate-600">of {c.totalSeats} seats available</span>
        </p>
        <div className="mt-2">
          <SeatMeter held={c.heldSeats} confirmed={c.confirmedSeats} total={c.totalSeats} label={`${c.name} seat utilization`} />
        </div>
      </div>

      <dl className="grid grid-cols-3 gap-2 text-center text-xs text-slate-600">
        <div className="rounded-lg bg-slate-50 p-2"><dt>Total</dt><dd className="text-sm font-semibold text-slate-900">{c.totalSeats}</dd></div>
        <div className="rounded-lg bg-slate-50 p-2"><dt>Held</dt><dd className="text-sm font-semibold text-slate-900">{c.heldSeats}</dd></div>
        <div className="rounded-lg bg-slate-50 p-2"><dt>Confirmed</dt><dd className="text-sm font-semibold text-slate-900">{c.confirmedSeats}</dd></div>
      </dl>

      <div className="mt-auto flex items-center gap-2">
        {applicationStatus && <StatusBadge status={applicationStatus} />}
        <button
          type="button"
          disabled={button.disabled}
          onClick={() => onApply(c)}
          className="ml-auto inline-flex items-center justify-center gap-2 rounded-lg bg-indigo-700 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-800 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-indigo-700 disabled:cursor-not-allowed disabled:bg-slate-300 disabled:text-slate-700"
        >
          {button.label}
        </button>
      </div>
    </article>
  )
}

