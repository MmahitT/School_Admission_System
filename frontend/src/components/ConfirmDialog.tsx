import { useEffect, useId, useRef, type ReactNode } from 'react'
import { Loader2 } from 'lucide-react'

interface Props {
  open: boolean
  title: string
  children: ReactNode
  confirmLabel: string
  cancelLabel?: string
  busy?: boolean
  tone?: 'primary' | 'danger'
  onConfirm: () => void
  onCancel: () => void
}

/** Accessible modal confirmation. Escape and backdrop clicks cancel, unless an action is in flight. */
export function ConfirmDialog({
  open,
  title,
  children,
  confirmLabel,
  cancelLabel = 'Cancel',
  busy = false,
  tone = 'primary',
  onConfirm,
  onCancel,
}: Props) {
  const titleId = useId()
  const confirmRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) return
    confirmRef.current?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !busy) onCancel()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, busy, onCancel])

  if (!open) return null

  const confirmStyle =
    tone === 'danger' ? 'bg-red-700 hover:bg-red-800 focus-visible:outline-red-700' : 'bg-indigo-700 hover:bg-indigo-800 focus-visible:outline-indigo-700'

  return (
    <div className="fixed inset-0 z-40 flex items-end justify-center bg-slate-900/50 p-4 sm:items-center" onMouseDown={(e) => { if (e.target === e.currentTarget && !busy) onCancel() }}>
      <div role="dialog" aria-modal="true" aria-labelledby={titleId} className="w-full max-w-md rounded-xl bg-white p-6 shadow-xl">
        <h2 id={titleId} className="text-lg font-semibold text-slate-900">{title}</h2>
        <div className="mt-3 space-y-3 text-sm text-slate-700">{children}</div>
        <div className="mt-6 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <button type="button" onClick={onCancel} disabled={busy} className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50">
            {cancelLabel}
          </button>
          <button
            ref={confirmRef}
            type="button"
            onClick={onConfirm}
            disabled={busy}
            className={`inline-flex items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm font-semibold text-white focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-60 ${confirmStyle}`}
          >
            {busy && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
