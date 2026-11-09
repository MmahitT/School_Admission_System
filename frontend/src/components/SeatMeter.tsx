import { utilizationPercent } from '../utils/seats'

interface Props {
  held: number
  confirmed: number
  total: number
  label?: string
}

/** Stacked bar: confirmed (dark) + held (light) out of total capacity. */
export function SeatMeter({ held, confirmed, total, label = 'Seat utilization' }: Props) {
  const percent = utilizationPercent(held, confirmed, total)
  const confirmedWidth = total > 0 ? Math.min(100, (confirmed / total) * 100) : 0
  const heldWidth = total > 0 ? Math.min(100 - confirmedWidth, (held / total) * 100) : 0
  return (
    <div role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent} className="flex h-2.5 w-full overflow-hidden rounded-full bg-slate-200">
      <div className="bg-indigo-700" style={{ width: `${confirmedWidth}%` }} />
      <div className="bg-amber-400" style={{ width: `${heldWidth}%` }} />
    </div>
  )
}
