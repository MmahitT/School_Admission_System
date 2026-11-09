import { useForm } from 'react-hook-form'
import { ArrowRight } from 'lucide-react'
import { isValidStudentId, STUDENT_ID_ERROR } from '../../utils/studentId'

interface Props {
  defaultValue?: string
  submitLabel?: string
  onSubmit: (studentId: string) => void
}

interface FormValues {
  studentId: string
}

export function StudentIdForm({ defaultValue = '', submitLabel = 'Continue', onSubmit }: Props) {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ defaultValues: { studentId: defaultValue } })

  return (
    <form onSubmit={handleSubmit(({ studentId }) => onSubmit(studentId.trim()))} noValidate className="space-y-3">
      <div>
        <label htmlFor="studentId" className="block text-sm font-medium text-slate-800">
          Student birth registration number
        </label>
        <input
          id="studentId"
          type="text"
          inputMode="numeric"
          autoComplete="off"
          placeholder="17 digits, e.g. 20152691234567890"
          aria-invalid={errors.studentId ? 'true' : 'false'}
          aria-describedby="studentId-help"
          className="mt-1 block w-full rounded-lg border border-slate-400 px-3 py-2.5 font-mono text-base tracking-wide text-slate-900 placeholder:font-sans placeholder:tracking-normal placeholder:text-slate-500 focus:border-indigo-700 focus:outline-2 focus:outline-indigo-700"
          {...register('studentId', {
            validate: (v) => isValidStudentId(v.trim()) || STUDENT_ID_ERROR,
          })}
        />
        <p id="studentId-help" className="mt-1 text-xs text-slate-600">Exactly 17 digits. Use the same number every time to see your applications.</p>
        {errors.studentId && (
          <p role="alert" className="mt-1 text-sm font-medium text-red-700">
            {errors.studentId.message}
          </p>
        )}
      </div>
      <button type="submit" className="inline-flex w-full items-center justify-center gap-2 rounded-lg bg-indigo-700 px-4 py-2.5 text-sm font-semibold text-white hover:bg-indigo-800 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-indigo-700">
        {submitLabel}
        <ArrowRight className="h-4 w-4" aria-hidden="true" />
      </button>
    </form>
  )
}
