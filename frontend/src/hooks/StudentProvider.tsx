import { useCallback, useMemo, useState, type ReactNode } from 'react'
import { clearStudentId, isValidStudentId, loadStudentId, saveStudentId } from '../utils/studentId'
import { StudentContext } from './studentContext'

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
