import type { AdmissionClass, CreatedClass, CreateHoldResponse, MyHold } from '../types/api'
import { http } from './client'

const asStudent = (studentId: string) => ({ headers: { 'X-Student-Id': studentId } })

export const getClasses = () => http.get<AdmissionClass[]>('/classes').then((r) => r.data)

export const createClass = (name: string, totalSeats: number) =>
  http.post<CreatedClass>('/classes', { name, totalSeats }).then((r) => r.data)

export const createHold = (classId: string, studentId: string) =>
  http.post<CreateHoldResponse>(`/classes/${classId}/holds`, undefined, asStudent(studentId)).then((r) => r.data)

export const confirmHold = (holdId: string, studentId: string) =>
  http.post<{ holdId: string; status: string }>(`/holds/${holdId}/confirm`, undefined, asStudent(studentId)).then((r) => r.data)

export const cancelHold = (holdId: string, studentId: string) =>
  http.delete(`/holds/${holdId}`, asStudent(studentId)).then(() => undefined)

export const getMyHolds = (studentId: string) =>
  http.get<MyHold[]>('/me/holds', asStudent(studentId)).then((r) => r.data)
