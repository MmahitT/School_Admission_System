import { useEffect, useRef, useState } from 'react'
import { ShieldAlert } from 'lucide-react'
import { cancelHold, confirmHold } from '../../api/admission'
import { toUserMessage } from '../../api/errors'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { Skeleton } from '../../components/Skeleton'
import { useToast } from '../../components/Toast'
import type { MyHold } from '../../types/api'
import { HoldCard } from './HoldCard'

interface Props {
  studentId: string
  holds: MyHold[] | null
  loading: boolean
  now: number
  onChanged: () => Promise<unknown> | void
}

export function MyApplications({ studentId, holds, loading, now, onChanged }: Props) {
  const toast = useToast()
  const [payTarget, setPayTarget] = useState<MyHold | null>(null)
  const [cancelTarget, setCancelTarget] = useState<MyHold | null>(null)
  const [busy, setBusy] = useState(false)
  const inFlight = useRef(false) // guards against double clicks before state updates render
  const refreshedExpired = useRef(new Set<string>())

  // When a countdown reaches zero, ask the server for the authoritative status (once per hold).
  useEffect(() => {
    const newlyExpired = (holds ?? []).filter(
      (h) => h.status === 'HELD' && Date.parse(h.expiresAt) <= now && !refreshedExpired.current.has(h.holdId),
    )
    if (newlyExpired.length === 0) return
    newlyExpired.forEach((h) => refreshedExpired.current.add(h.holdId))
    void onChanged()
  }, [holds, now, onChanged])

  async function run(action: () => Promise<void>, successMessage: string) {
    if (inFlight.current) return
    inFlight.current = true
    setBusy(true)
    try {
      await action()
      toast.success(successMessage)
    } catch (e) {
      toast.error(toUserMessage(e))
    } finally {
      inFlight.current = false
      setBusy(false)
      setPayTarget(null)
      setCancelTarget(null)
      await onChanged()
    }
  }

  const pay = () =>
    payTarget &&
    run(
      async () => { await confirmHold(payTarget.holdId, studentId) },
      `Payment successful (simulated). Admission confirmed for ${payTarget.className}.`,
    )
  const cancel = () =>
    cancelTarget && run(() => cancelHold(cancelTarget.holdId, studentId), `Reservation for ${cancelTarget.className} was cancelled.`)

  if (loading && holds === null) {
    return (
      <div role="status" aria-label="Loading applications" className="space-y-3">
        <Skeleton className="h-28 w-full" />
      </div>
    )
  }

  if (!holds || holds.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-slate-300 bg-white p-6 text-center text-slate-600">
        <p className="font-medium text-slate-800">You have not applied to any class yet.</p>
        <p className="mt-1 text-sm">Choose a class below and press Apply to reserve a seat.</p>
      </div>
    )
  }

  return (
    <>
      <ul className="grid gap-4 lg:grid-cols-2" aria-label="My applications">
        {holds.map((h) => (
          <HoldCard key={h.holdId} hold={h} now={now} disabled={busy} onPay={setPayTarget} onCancel={setCancelTarget} />
        ))}
      </ul>

      <ConfirmDialog
        open={payTarget !== null}
        title="Simulated payment"
        confirmLabel="Confirm simulated payment"
        busy={busy}
        onConfirm={pay}
        onCancel={() => setPayTarget(null)}
      >
        <div className="flex gap-2 rounded-lg bg-sky-50 p-3 text-sky-900">
          <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <p>This is a <strong>simulated payment</strong>. No real money is charged and no card details are collected.</p>
        </div>
        <p>
          Paying the admission fee for <strong>{payTarget?.className}</strong> will confirm your admission.
        </p>
      </ConfirmDialog>

      <ConfirmDialog
        open={cancelTarget !== null}
        title="Cancel this reservation?"
        confirmLabel="Yes, cancel reservation"
        cancelLabel="Keep reservation"
        tone="danger"
        busy={busy}
        onConfirm={cancel}
        onCancel={() => setCancelTarget(null)}
      >
        <p>
          Your seat in <strong>{cancelTarget?.className}</strong> will be released. You will not be able to apply to this class again.
        </p>
      </ConfirmDialog>
    </>
  )
}
