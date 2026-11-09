import axios from 'axios'
import type { ApiErrorBody } from '../types/api'

export const GENERIC_ERROR = 'Something went wrong. Please try again.'
export const NETWORK_ERROR = 'Unable to reach the server. Please check your connection and try again.'

// User-facing wording by stable backend error code (spec section 10).
const MESSAGES: Record<string, string> = {
  INVALID_STUDENT_ID: 'Please enter a valid 17-digit birth registration number.',
  NO_SEATS_AVAILABLE: 'No seats are currently available for this class.',
  ACTIVE_HOLD_EXISTS: 'You already have an active admission reservation.',
  ALREADY_APPLIED: 'You have already applied to this class.',
  HOLD_EXPIRED: 'Your reservation has expired. Please apply again if seats are available.',
  HOLD_CANCELLED: 'This reservation was cancelled and cannot be used.',
  HOLD_CONFIRMED: 'A confirmed admission cannot be cancelled.',
  NOT_HOLD_OWNER: 'This reservation belongs to a different student.',
  CLASS_NOT_FOUND: 'The requested class was not found.',
  HOLD_NOT_FOUND: 'The requested reservation was not found.',
  DUPLICATE_CLASS_NAME: 'A class with this name already exists.',
}

export function getErrorCode(error: unknown): string | undefined {
  if (!axios.isAxiosError(error)) return undefined
  return (error.response?.data as Partial<ApiErrorBody> | undefined)?.code
}

/** Converts any thrown value into a message that is safe and understandable to show to a user. */
export function toUserMessage(error: unknown): string {
  if (!axios.isAxiosError(error)) return GENERIC_ERROR
  if (!error.response) return NETWORK_ERROR

  const body = error.response.data as Partial<ApiErrorBody> | undefined
  if (error.response.status >= 500) return GENERIC_ERROR
  if (body?.code && MESSAGES[body.code]) return MESSAGES[body.code]
  if (body?.code === 'VALIDATION_FAILED' && typeof body.detail === 'string') return body.detail
  return GENERIC_ERROR
}
