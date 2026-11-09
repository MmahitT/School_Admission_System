export const STUDENT_ID_PATTERN = /^\d{17}$/
export const STUDENT_ID_ERROR = 'Please enter a valid 17-digit birth registration number.'
export const STUDENT_ID_STORAGE_KEY = 'studentId'

export function isValidStudentId(value: string | null | undefined): value is string {
  return typeof value === 'string' && STUDENT_ID_PATTERN.test(value)
}

// localStorage can throw (private mode, blocked storage); the app must still work without it.
export function loadStudentId(): string | null {
  try {
    const value = window.localStorage.getItem(STUDENT_ID_STORAGE_KEY)
    return isValidStudentId(value) ? value : null
  } catch {
    return null
  }
}

export function saveStudentId(value: string): void {
  try {
    window.localStorage.setItem(STUDENT_ID_STORAGE_KEY, value)
  } catch {
    /* ignore */
  }
}

export function clearStudentId(): void {
  try {
    window.localStorage.removeItem(STUDENT_ID_STORAGE_KEY)
  } catch {
    /* ignore */
  }
}
