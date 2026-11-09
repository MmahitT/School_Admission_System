import type { InternalAxiosRequestConfig } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { resetServerClock, serverNow } from '../utils/serverClock'
import { cancelHold, confirmHold, createClass, createHold, getClasses, getMyHolds } from './admission'
import { http } from './client'

const STUDENT = '20152691234567890'
let requests: InternalAxiosRequestConfig[]
let responseHeaders: Record<string, string>
let responseData: unknown

beforeEach(() => {
  requests = []
  responseHeaders = {}
  responseData = []
  // Replace the network layer so the real axios instance, interceptors and request building are exercised.
  http.defaults.adapter = async (config) => {
    requests.push(config)
    return { data: responseData, status: 200, statusText: 'OK', headers: responseHeaders, config }
  }
  resetServerClock()
})
afterEach(() => vi.useRealTimers())

const header = (c: InternalAxiosRequestConfig, name: string) => c.headers.get(name)

describe('API contract', () => {
  it('uses the configurable base URL (default /api, proxied by nginx)', async () => {
    await getClasses()
    expect(requests[0].baseURL).toBe('/api')
    expect(requests[0].url).toBe('/classes')
    expect(requests[0].method).toBe('get')
  })

  it('GET /classes sends no student header', async () => {
    await getClasses()
    expect(header(requests[0], 'X-Student-Id')).toBeFalsy()
  })

  it('POST /classes sends name and totalSeats', async () => {
    responseData = { id: 'c1', name: 'Class 6', totalSeats: 120 }
    const created = await createClass('Class 6', 120)
    expect(requests[0].method).toBe('post')
    expect(requests[0].url).toBe('/classes')
    expect(JSON.parse(requests[0].data)).toEqual({ name: 'Class 6', totalSeats: 120 })
    expect(created.id).toBe('c1')
  })

  it('POST /classes/{id}/holds carries the X-Student-Id header', async () => {
    await createHold('c1', STUDENT)
    expect(requests[0].method).toBe('post')
    expect(requests[0].url).toBe('/classes/c1/holds')
    expect(header(requests[0], 'X-Student-Id')).toBe(STUDENT)
  })

  it('POST /holds/{id}/confirm carries the header', async () => {
    await confirmHold('h1', STUDENT)
    expect(requests[0].method).toBe('post')
    expect(requests[0].url).toBe('/holds/h1/confirm')
    expect(header(requests[0], 'X-Student-Id')).toBe(STUDENT)
  })

  it('DELETE /holds/{id} carries the header and resolves to nothing (204)', async () => {
    await expect(cancelHold('h1', STUDENT)).resolves.toBeUndefined()
    expect(requests[0].method).toBe('delete')
    expect(requests[0].url).toBe('/holds/h1')
    expect(header(requests[0], 'X-Student-Id')).toBe(STUDENT)
  })

  it('GET /me/holds carries the header', async () => {
    await getMyHolds(STUDENT)
    expect(requests[0].url).toBe('/me/holds')
    expect(header(requests[0], 'X-Student-Id')).toBe(STUDENT)
  })
})

describe('server clock synchronisation', () => {
  it('learns the server/browser offset from the Date header of a response', async () => {
    vi.useFakeTimers({ now: Date.parse('2026-11-01T03:59:00Z'), toFake: ['Date'] }) // browser clock is 60 s behind
    responseHeaders = { date: 'Sun, 01 Nov 2026 04:00:00 GMT' }
    await getClasses()
    // server time is the header second plus the 500 ms midpoint correction
    expect(serverNow() - Date.now()).toBe(60_000 + 500)
  })

  it('ignores responses without a usable Date header', async () => {
    responseHeaders = { date: 'not a date' }
    await getClasses()
    expect(Math.abs(serverNow() - Date.now())).toBeLessThan(50)
  })
})
