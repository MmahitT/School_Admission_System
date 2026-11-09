import { describe, expect, it } from 'vitest'
import { axiosError } from '../test/render'
import { GENERIC_ERROR, NETWORK_ERROR, toUserMessage } from './errors'

describe('toUserMessage', () => {
  it.each([
    ['NO_SEATS_AVAILABLE', 'No seats are currently available for this class.'],
    ['ACTIVE_HOLD_EXISTS', 'You already have an active admission reservation.'],
    ['HOLD_EXPIRED', 'Your reservation has expired. Please apply again if seats are available.'],
    ['INVALID_STUDENT_ID', 'Please enter a valid 17-digit birth registration number.'],
  ])('maps %s', (code, message) => expect(toUserMessage(axiosError(409, code))).toBe(message))

  it('shows server validation detail for VALIDATION_FAILED', () => {
    expect(toUserMessage(axiosError(400, 'VALIDATION_FAILED', 'Class name is required.'))).toBe('Class name is required.')
  })

  it('never leaks server details for 5xx', () => {
    const msg = toUserMessage(axiosError(500, 'INTERNAL_ERROR', 'Npgsql.PostgresException: boom'))
    expect(msg).toBe(GENERIC_ERROR)
    expect(msg).not.toMatch(/Npgsql/)
  })

  it('handles network failures and unknown errors', () => {
    expect(toUserMessage(Object.assign(new Error('Network Error'), { isAxiosError: true }))).toBe(NETWORK_ERROR)
    expect(toUserMessage(new Error('x'))).toBe(GENERIC_ERROR)
    expect(toUserMessage(axiosError(409, 'SOMETHING_NEW'))).toBe(GENERIC_ERROR)
  })
})
