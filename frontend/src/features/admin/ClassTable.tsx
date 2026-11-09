import { SeatMeter } from '../../components/SeatMeter'
import type { AdmissionClass } from '../../types/api'
import { utilizationPercent } from '../../utils/seats'

export function ClassTable({ classes }: { classes: AdmissionClass[] }) {
  if (classes.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-slate-300 bg-white p-8 text-center text-slate-600">
        <p className="font-medium text-slate-800">No classes yet.</p>
        <p className="mt-1 text-sm">Create the first class using the form above.</p>
      </div>
    )
  }
  return (
    <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white shadow-sm">
      <table className="w-full min-w-[40rem] text-left text-sm">
        <caption className="sr-only">Classes and seat utilization</caption>
        <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-600">
          <tr>
            <th scope="col" className="px-4 py-3">Class / shift</th>
            <th scope="col" className="px-4 py-3 text-right">Total</th>
            <th scope="col" className="px-4 py-3 text-right">Held</th>
            <th scope="col" className="px-4 py-3 text-right">Confirmed</th>
            <th scope="col" className="px-4 py-3 text-right">Available</th>
            <th scope="col" className="w-48 px-4 py-3">Utilization</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {classes.map((c) => (
            <tr key={c.id}>
              <th scope="row" className="px-4 py-3 font-medium text-slate-900">{c.name}</th>
              <td className="px-4 py-3 text-right tabular-nums">{c.totalSeats}</td>
              <td className="px-4 py-3 text-right tabular-nums">{c.heldSeats}</td>
              <td className="px-4 py-3 text-right tabular-nums">{c.confirmedSeats}</td>
              <td className="px-4 py-3 text-right font-semibold tabular-nums">{c.availableSeats}</td>
              <td className="px-4 py-3">
                <div className="flex items-center gap-2">
                  <div className="flex-1"><SeatMeter held={c.heldSeats} confirmed={c.confirmedSeats} total={c.totalSeats} label={`${c.name} utilization`} /></div>
                  <span className="w-10 text-right text-xs tabular-nums text-slate-700">{utilizationPercent(c.heldSeats, c.confirmedSeats, c.totalSeats)}%</span>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
