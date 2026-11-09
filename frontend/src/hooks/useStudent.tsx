import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { clearStudentId, isValidStudentId, loadStudentId, saveStudentId } from '../utils/studentId'

interface StudentContextValue {
  studentId: string | null
  signIn: (id: string) => boolean
  signOut: () => void
}

const StudentContext = createContext<StudentContextValue | null>(null)

export function StudentProvider({ children }: { children: ReactNode }) {
  const [studentId, setStudentId] = useState<string | null>(() => loadStudentId())

  const signIn = useCallback((id: string) => {
    if (!isValidStudentId(id)) return false
    saveStudentId(id)
    setStudentId(id)
    return true
  }, [])

  const signOut = useCallback(() => {
    clearStudentId()
    setStudentId(null)
  }, [])

  const value = useMemo(() => ({ studentId, signIn, signOut }), [studentId, signIn, signOut])
  return <StudentContext.Provider value={value}>{children}</StudentContext.Provider>
}

export function useStudent(): StudentContextValue {
  const ctx = useContext(StudentContext)
  if (!ctx) throw new Error('useStudent must be used inside <StudentProvider>')
  return ctx
}
