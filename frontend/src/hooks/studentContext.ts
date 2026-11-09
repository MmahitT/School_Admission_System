import { createContext } from 'react'

export interface StudentContextValue {
  studentId: string | null
  signIn: (id: string) => boolean
  signOut: () => void
}

export const StudentContext = createContext<StudentContextValue | null>(null)
