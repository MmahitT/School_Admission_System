import { beforeEach, describe, expect, it, vi } from 'vitest'
import { resetServerClock, serverNow, setServerTime } from './serverClock'
import { seatState, utilizationPercent } from './seats'
import { clearStudentId, isValidStudentId, loadStudentId, saveStudentId } from './studentId'
import { effectiveStatus, formatRemaining } from './time'

describe('student id validation', () => {
  it.each(['20152691234567890', '00000000000000000'])('accepts %s', (v) => expect(isValidStudentId(v)).toBe(true))
  it.each(['', '123', '2015269123456789', '201526912345678901', '2015269123456789a', ' 20152691234567890', '2015 2691234567890', null, undefined])(
    'rejects %j',
    (v) => expect(isValidStudentId(v as string)).toBe(false),
  )
})

describe('student id persistence', () => {
  it('round-trips through localStorage and ignores invalid stored values', () => {
    expect(loadStudentId()).toBeNull()
    saveStudentId('20152691234567890')
    expect(loadStudentId()).toBe('20152691234567890')
    window.localStorage.setItem('studentId', 'garbage')
    expect(loadStudentId()).toBeNull()
    saveStudentId('20152691234567890')
    clearStudentId()
    expect(loadStudentId()).toBeNull()
  })

  it('does not throw when storage is unavailable', () => {
    const spy = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked') })
    expect(loadStudentId()).toBeNull()
    spy.mockRestore()
  })
})

describe('seat helpers', () => {
  it('classifies availability', () => {
    expect(seatState(0, 100)).toBe('full')
    expect(seatState(5, 100)).toBe('limited')
    expect(seatState(10, 100)).toBe('limited')
    expect(seatState(11, 100)).toBe('available')
    expect(seatState(1, 1)).toBe('limited')
  })
  it('computes utilization within 0..100', () => {
    expect(utilizationPercent(10, 20, 120)).toBe(25)
    expect(utilizationPercent(200, 0, 100)).toBe(100)
    expect(utilizationPercent(1, 1, 0)).toBe(0)
  })
})

describe('time helpers', () => {
  it('formats remaining time and never goes negative', () => {
    expect(formatRemaining(600_000)).toBe('10:00')
    expect(formatRemaining(61_000)).toBe('01:01')
    expect(formatRemaining(599_900)).toBe('09:59')
    expect(formatRemaining(500)).toBe('00:00')
    expect(formatRemaining(-5000)).toBe('00:00')
  })
  it('treats a HELD hold at or past expiry as EXPIRED', () => {
    const t = Date.parse('2026-11-01T04:10:00Z')
    expect(effectiveStatus('HELD', '2026-11-01T04:10:00Z', t - 1)).toBe('HELD')
    expect(effectiveStatus('HELD', '2026-11-01T04:10:00Z', t)).toBe('EXPIRED')
    expect(effectiveStatus('CONFIRMED', '2026-11-01T04:10:00Z', t + 1e6)).toBe('CONFIRMED')
  })
})

describe('server clock', () => {
  beforeEach(resetServerClock)
  it('applies the offset learned from the server Date header', () => {
    setServerTime(1_000_000 + 5_000, 1_000_000)
    vi.useFakeTimers({ now: 2_000_000 })
    expect(serverNow()).toBe(2_005_000)
    vi.useRealTimers()
  })
})
