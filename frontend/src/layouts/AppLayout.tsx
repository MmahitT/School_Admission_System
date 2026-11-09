import { GraduationCap, LogOut, ShieldCheck, UserRound } from 'lucide-react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useStudent } from '../hooks/useStudent'

export const SCHOOL_NAME = 'Bright Future School'

const navClass = ({ isActive }: { isActive: boolean }) =>
  `inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm font-medium ${
    isActive ? 'bg-indigo-700 text-white' : 'text-slate-800 hover:bg-slate-100'
  }`

export function AppLayout() {
  const { studentId, signOut } = useStudent()
  const navigate = useNavigate()

  return (
    <div className="flex min-h-screen flex-col bg-slate-50 text-slate-900">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-3 px-4 py-3">
          <Link to="/" className="flex items-center gap-2.5">
            {/* Logo placeholder */}
            <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-indigo-700 text-white" aria-hidden="true">
              <GraduationCap className="h-5 w-5" />
            </span>
            <span className="text-base font-bold">{SCHOOL_NAME}</span>
          </Link>

          <nav aria-label="Main" className="flex items-center gap-1">
            <NavLink to={studentId ? '/dashboard' : '/'} end className={navClass}>
              <UserRound className="h-4 w-4" aria-hidden="true" /> Parent portal
            </NavLink>
            <NavLink to="/admin" className={navClass}>
              <ShieldCheck className="h-4 w-4" aria-hidden="true" /> Admin
            </NavLink>
          </nav>

          {studentId && (
            <div className="flex items-center gap-2 text-sm">
              <span className="rounded-lg bg-slate-100 px-2.5 py-1 font-mono text-slate-800" title="Current student">{studentId}</span>
              <button
                type="button"
                onClick={() => { signOut(); navigate('/') }}
                className="inline-flex items-center gap-1 rounded-lg px-2 py-1 font-medium text-slate-700 hover:bg-slate-100"
              >
                <LogOut className="h-4 w-4" aria-hidden="true" /> Change
              </button>
            </div>
          )}
        </div>
      </header>

      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-8">
        <Outlet />
      </main>

      <footer className="border-t border-slate-200 bg-white py-4 text-center text-xs text-slate-600">
        {SCHOOL_NAME} admission portal · Payments in this portal are simulated.
      </footer>
    </div>
  )
}
