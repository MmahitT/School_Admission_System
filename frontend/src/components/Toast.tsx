import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { AlertCircle, CheckCircle2, Info, X } from 'lucide-react'

type Kind = 'success' | 'error' | 'info'
interface ToastItem {
  id: number
  kind: Kind
  message: string
}
interface ToastApi {
  success: (message: string) => void
  error: (message: string) => void
  info: (message: string) => void
}

const ToastContext = createContext<ToastApi | null>(null)
const DISMISS_AFTER_MS = 6000

const STYLES: Record<Kind, string> = {
  success: 'border-emerald-300 bg-emerald-50 text-emerald-900',
  error: 'border-red-300 bg-red-50 text-red-900',
  info: 'border-sky-300 bg-sky-50 text-sky-900',
}
const ICONS = { success: CheckCircle2, error: AlertCircle, info: Info }

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([])
  const timers = useRef(new Map<number, ReturnType<typeof setTimeout>>())
  const counter = useRef(0)

  const dismiss = useCallback((id: number) => {
    const timer = timers.current.get(id)
    if (timer) clearTimeout(timer)
    timers.current.delete(id)
    setToasts((list) => list.filter((t) => t.id !== id))
  }, [])

  const push = useCallback(
    (kind: Kind, message: string) => {
      const id = ++counter.current
      setToasts((list) => [...list, { id, kind, message }])
      timers.current.set(id, setTimeout(() => dismiss(id), DISMISS_AFTER_MS))
    },
    [dismiss],
  )

  useEffect(() => {
    const active = timers.current
    return () => active.forEach((t) => clearTimeout(t))
  }, [])

  const api = useMemo<ToastApi>(
    () => ({
      success: (m) => push('success', m),
      error: (m) => push('error', m),
      info: (m) => push('info', m),
    }),
    [push],
  )

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div
        className="pointer-events-none fixed inset-x-0 bottom-0 z-50 flex flex-col items-center gap-2 p-4 sm:items-end"
        aria-live="polite"
      >
        {toasts.map((t) => {
          const Icon = ICONS[t.kind]
          return (
            <div
              key={t.id}
              role={t.kind === 'error' ? 'alert' : 'status'}
              className={`pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-lg border p-3 shadow-lg ${STYLES[t.kind]}`}
            >
              <Icon className="mt-0.5 h-5 w-5 shrink-0" aria-hidden="true" />
              <p className="flex-1 text-sm">{t.message}</p>
              <button type="button" onClick={() => dismiss(t.id)} aria-label="Dismiss notification" className="rounded p-0.5 hover:bg-black/5">
                <X className="h-4 w-4" aria-hidden="true" />
              </button>
            </div>
          )
        })}
      </div>
    </ToastContext.Provider>
  )
}

export function useToast(): ToastApi {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast must be used inside <ToastProvider>')
  return ctx
}
