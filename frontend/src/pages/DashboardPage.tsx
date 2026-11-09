import { AlertCircle, RefreshCw } from 'lucide-react'
import { Navigate } from 'react-router-dom'
import { getClasses, getMyHolds } from '../api/admission'
import { toUserMessage } from '../api/errors'
import { CardSkeletons } from '../components/Skeleton'
import { ClassList } from '../features/classes/ClassList'
import { MyApplications } from '../features/holds/MyApplications'
import { usePolledResource } from '../hooks/usePolledResource'
import { useServerNow } from '../hooks/useServerNow'
import { useStudent } from '../hooks/useStudent'

const POLL_MS = 5000

export default function DashboardPage() {
  const { studentId } = useStudent()
  if (!studentId) return <Navigate to="/" replace />
  return <Dashboard studentId={studentId} />
}

function Dashboard({ studentId }: { studentId: string }) {
  const now = useServerNow(1000)
  const classes = usePolledResource(getClasses, POLL_MS, studentId)
  const holds = usePolledResource(() => getMyHolds(studentId), POLL_MS, studentId)

  const refreshAll = async () => {
    await Promise.all([classes.refresh(), holds.refresh()])
  }
  const failed = classes.error ?? holds.error

  return (
    <div className="space-y-10">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-slate-900">Admission dashboard</h1>
          <p className="text-sm text-slate-700">Student: <span className="font-mono">{studentId}</span></p>
        </div>
        <button type="button" onClick={() => void refreshAll()} className="inline-flex items-center gap-2 rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm font-medium text-slate-800 hover:bg-slate-50">
          <RefreshCw className="h-4 w-4" aria-hidden="true" /> Refresh
        </button>
      </div>

      {failed != null && (
        <div role="alert" className="flex items-start gap-2 rounded-lg border border-red-300 bg-red-50 p-3 text-sm text-red-900">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <p>{toUserMessage(failed)}</p>
        </div>
      )}

      <section aria-labelledby="apps-heading" className="space-y-3">
        <h2 id="apps-heading" className="text-lg font-semibold text-slate-900">My applications</h2>
        <MyApplications studentId={studentId} holds={holds.data} loading={holds.loading} now={now} onChanged={refreshAll} />
      </section>

      <section aria-labelledby="classes-heading" className="space-y-3">
        <h2 id="classes-heading" className="text-lg font-semibold text-slate-900">Available classes</h2>
        {classes.data === null ? (
          classes.loading ? <CardSkeletons /> : null
        ) : (
          <ClassList classes={classes.data} holds={holds.data ?? []} studentId={studentId} now={now} onChanged={refreshAll} />
        )}
      </section>
    </div>
  )
}
