import { AlertCircle, RefreshCw, ShieldAlert } from 'lucide-react'
import { getClasses } from '../api/admission'
import { toUserMessage } from '../api/errors'
import { Skeleton } from '../components/Skeleton'
import { ClassTable } from '../features/admin/ClassTable'
import { CreateClassForm } from '../features/admin/CreateClassForm'
import { usePolledResource } from '../hooks/usePolledResource'

export default function AdminPage() {
  const classes = usePolledResource(getClasses, 5000)
  const totals = (classes.data ?? []).reduce(
    (a, c) => ({ total: a.total + c.totalSeats, held: a.held + c.heldSeats, confirmed: a.confirmed + c.confirmedSeats, available: a.available + c.availableSeats }),
    { total: 0, held: 0, confirmed: 0, available: 0 },
  )

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h1 className="text-2xl font-bold text-slate-900">School admin dashboard</h1>
        <button type="button" onClick={() => void classes.refresh()} className="inline-flex items-center gap-2 rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm font-medium text-slate-800 hover:bg-slate-50">
          <RefreshCw className="h-4 w-4" aria-hidden="true" /> Refresh
        </button>
      </div>

      <p className="flex items-start gap-2 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
        <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
        Demo mode: this admin area has no login, as permitted by the assignment. Do not expose it publicly.
      </p>

      <section aria-labelledby="create-heading" className="space-y-3">
        <h2 id="create-heading" className="text-lg font-semibold text-slate-900">Create a class</h2>
        <CreateClassForm onCreated={classes.refresh} />
      </section>

      <section aria-labelledby="overview-heading" className="space-y-3">
        <h2 id="overview-heading" className="text-lg font-semibold text-slate-900">Classes and utilization</h2>
        {classes.error != null && (
          <div role="alert" className="flex items-start gap-2 rounded-lg border border-red-300 bg-red-50 p-3 text-sm text-red-900">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
            <p>{toUserMessage(classes.error)}</p>
          </div>
        )}
        {classes.data === null ? (
          classes.loading ? <div role="status" aria-label="Loading classes" className="space-y-2"><Skeleton className="h-10 w-full" /><Skeleton className="h-10 w-full" /></div> : null
        ) : (
          <>
            <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              {[['Total seats', totals.total], ['Held', totals.held], ['Confirmed', totals.confirmed], ['Available', totals.available]].map(([label, value]) => (
                <div key={label} className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
                  <dt className="text-xs uppercase tracking-wide text-slate-600">{label}</dt>
                  <dd className="text-2xl font-bold tabular-nums text-slate-900">{value}</dd>
                </div>
              ))}
            </dl>
            <ClassTable classes={classes.data} />
          </>
        )}
      </section>
    </div>
  )
}
