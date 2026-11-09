export type HoldStatus = 'HELD' | 'CONFIRMED' | 'EXPIRED' | 'CANCELLED'

export interface AdmissionClass {
  id: string
  name: string
  totalSeats: number
  heldSeats: number
  confirmedSeats: number
  availableSeats: number
}

export interface CreatedClass {
  id: string
  name: string
  totalSeats: number
}

export interface CreateHoldResponse {
  holdId: string
  classId: string
  studentId: string
  status: HoldStatus
  expiresAt: string
}

export interface MyHold {
  holdId: string
  classId: string
  className: string
  status: HoldStatus
  expiresAt: string
}

export interface ApiErrorBody {
  status: number
  title: string
  detail: string
  code: string
  traceId?: string
}
