import { useState } from 'react'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { useToast } from '../../components/useToast'
import { createHold } from '../../api/admission'
import { toUserMessage } from '../../api/errors'
import type { AdmissionClass, HoldStatus, MyHold } from '../../types/api'
import { effectiveStatus } from '../../utils/time'
import { ClassCard } from './ClassCard'

interface Props {
  classes: AdmissionClass[]
  holds: MyHold[]
  studentId: string
  now: number
  onChanged: () => Promise<unknown> | void
}

export function ClassList({ classes, holds, studentId, now, onChanged }: Props) {
  const toast = useToast()
  const [target, setTarget] = useState<AdmissionClass | null>(null)
  const [busy, setBusy] = useState(false)

  if (classes.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-slate-300 bg-white p-8 text-center text-slate-600">
        <p className="font-medium text-slate-800">No classes are open for admission yet.</p>
        <p className="mt-1 text-sm">Please check back soon.</p>
      </div>
    )
  }

  const withStatus = holds.map((h) => ({ ...h, status: effectiveStatus(h.status, h.expiresAt, now) }))
  const hasActiveHold = withStatus.some((h) => h.status === 'HELD')
  // An EXPIRED application does not block applying again; everything else does.
  const applicationFor = (classId: string): HoldStatus | undefined =>
    withStatus.find((h) => h.classId === classId && h.status !== 'EXPIRED')?.status

  async function apply() {
    if (!target || busy) return
    setBusy(true)
    try {
      await createHold(target.id, studentId)
      toast.success(`Seat reserved in ${target.name}. Complete payment before the reservation expires.`)
    } catch (e) {
      toast.error(toUserMessage(e))
    } finally {
      setBusy(false)
      setTarget(null)
      await onChanged() // seat numbers may have changed either way
    }
  }

  return (
    <>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {classes.map((c) => (
          <ClassCard
            key={c.id}
            admissionClass={c}
            applicationStatus={applicationFor(c.id)}
            hasOtherActiveHold={hasActiveHold}
            onApply={setTarget}
          />
        ))}
      </div>

      <ConfirmDialog
        open={target !== null}
        title="Apply for admission?"
        confirmLabel="Confirm application"
        busy={busy}
        onConfirm={apply}
        onCancel={() => setTarget(null)}
      >
        <p>
          You are applying for <strong>{target?.name}</strong>.
        </p>
        <p>
          A seat will be reserved for you for a limited time. You must pay the admission fee before the timer ends or the seat is released.
          You can hold only one reservation at a time.
        </p>
      </ConfirmDialog>
    </>
  )
}
