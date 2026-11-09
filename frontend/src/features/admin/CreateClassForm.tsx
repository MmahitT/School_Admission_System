import { useForm } from 'react-hook-form'
import { Loader2, Plus } from 'lucide-react'
import { createClass } from '../../api/admission'
import { toUserMessage } from '../../api/errors'
import { useToast } from '../../components/Toast'

interface FormValues {
  name: string
  totalSeats: string
}

const MAX_NAME = 200
const MAX_SEATS = 100_000

export function CreateClassForm({ onCreated }: { onCreated: () => Promise<unknown> | void }) {
  const toast = useToast()
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ defaultValues: { name: '', totalSeats: '' } })

  const submit = handleSubmit(async ({ name, totalSeats }) => {
    try {
      const created = await createClass(name.trim(), Number(totalSeats))
      toast.success(`Class "${created.name}" created with ${created.totalSeats} seats.`)
      reset()
      await onCreated()
    } catch (e) {
      toast.error(toUserMessage(e))
    }
  })

  return (
    <form onSubmit={submit} noValidate className="grid gap-4 rounded-xl border border-slate-200 bg-white p-5 shadow-sm sm:grid-cols-[1fr_12rem_auto] sm:items-start">
      <div>
        <label htmlFor="className" className="block text-sm font-medium text-slate-800">Class / shift name</label>
        <input
          id="className"
          type="text"
          placeholder="Class 6 - Morning Shift"
          aria-invalid={errors.name ? 'true' : 'false'}
          className="mt-1 block w-full rounded-lg border border-slate-400 px-3 py-2 text-slate-900 placeholder:text-slate-500 focus:border-indigo-700 focus:outline-2 focus:outline-indigo-700"
          {...register('name', {
            validate: (v) => {
              const t = v.trim()
              if (!t) return 'Class name is required.'
              if (t.length > MAX_NAME) return `Class name must be at most ${MAX_NAME} characters.`
              return true
            },
          })}
        />
        {errors.name && <p role="alert" className="mt-1 text-sm font-medium text-red-700">{errors.name.message}</p>}
      </div>

      <div>
        <label htmlFor="totalSeats" className="block text-sm font-medium text-slate-800">Total seats</label>
        <input
          id="totalSeats"
          type="text"
          inputMode="numeric"
          placeholder="120"
          aria-invalid={errors.totalSeats ? 'true' : 'false'}
          className="mt-1 block w-full rounded-lg border border-slate-400 px-3 py-2 text-slate-900 placeholder:text-slate-500 focus:border-indigo-700 focus:outline-2 focus:outline-indigo-700"
          {...register('totalSeats', {
            validate: (v) => {
              const t = v.trim()
              if (!/^\d+$/.test(t)) return 'Enter a whole number of seats.'
              const n = Number(t)
              if (n <= 0) return 'Total seats must be greater than zero.'
              if (n > MAX_SEATS) return `Total seats must be at most ${MAX_SEATS}.`
              return true
            },
          })}
        />
        {errors.totalSeats && <p role="alert" className="mt-1 text-sm font-medium text-red-700">{errors.totalSeats.message}</p>}
      </div>

      <button
        type="submit"
        disabled={isSubmitting}
        className="inline-flex items-center justify-center gap-2 self-end rounded-lg bg-indigo-700 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-800 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-indigo-700 disabled:cursor-not-allowed disabled:opacity-60 sm:mt-6"
      >
        {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Plus className="h-4 w-4" aria-hidden="true" />}
        Create class
      </button>
    </form>
  )
}
