import { CalendarClock, CreditCard, GraduationCap, ListChecks } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { StudentIdForm } from '../features/admission/StudentIdForm'
import { useStudent } from '../hooks/useStudent'
import { SCHOOL_NAME } from '../layouts/AppLayout'

const STEPS = [
  { Icon: ListChecks, title: 'Choose a class', text: 'Pick the class and shift. Seats are limited and given first come, first served.' },
  { Icon: CalendarClock, title: 'Reserve your seat', text: 'Your seat is held for a short time while you complete payment.' },
  { Icon: CreditCard, title: 'Pay to confirm', text: 'Pay the admission fee before the timer ends. If it runs out, the seat is released.' },
]

export default function LandingPage() {
  const { studentId, signIn } = useStudent()
  const navigate = useNavigate()

  return (
    <div className="grid gap-10 lg:grid-cols-2 lg:items-center">
      <section>
        <span className="flex h-14 w-14 items-center justify-center rounded-2xl bg-indigo-700 text-white" aria-hidden="true">
          <GraduationCap className="h-8 w-8" />
        </span>
        <p className="mt-4 text-sm font-semibold uppercase tracking-wide text-indigo-800">{SCHOOL_NAME}</p>
        <h1 className="mt-1 text-3xl font-bold tracking-tight text-slate-900 sm:text-4xl">Admission Portal</h1>
        <p className="mt-3 max-w-prose text-slate-700">
          Apply online for the new academic year. Admission is first come, first served — there is no lottery. Enter the student&apos;s birth
          registration number to get started.
        </p>
        <ol className="mt-6 space-y-4">
          {STEPS.map(({ Icon, title, text }) => (
            <li key={title} className="flex gap-3">
              <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-indigo-100 text-indigo-800" aria-hidden="true"><Icon className="h-5 w-5" /></span>
              <div>
                <p className="font-semibold text-slate-900">{title}</p>
                <p className="text-sm text-slate-700">{text}</p>
              </div>
            </li>
          ))}
        </ol>
      </section>

      <section aria-labelledby="start-heading" className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
        <h2 id="start-heading" className="text-lg font-semibold text-slate-900">Start your application</h2>
        {studentId && <p className="mt-1 text-sm text-slate-700">Welcome back. Continue as the saved student or enter a different number.</p>}
        <div className="mt-4">
          <StudentIdForm
            defaultValue={studentId ?? ''}
            onSubmit={(id) => {
              if (signIn(id)) navigate('/dashboard')
            }}
          />
        </div>
      </section>
    </div>
  )
}
