import { act, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../api/admission'
import App from '../App'
import { axiosError, STUDENT } from '../test/render'
import type { MyHold } from '../types/api'
import { resetServerClock } from '../utils/serverClock'

vi.mock('../api/admission')

const START = Date.parse('2026-11-01T04:00:00Z')
const cls = { id: 'c1', name: 'Class 6 - Morning Shift', totalSeats: 1, heldSeats: 1, confirmedSeats: 0, availableSeats: 0 }
const hold = (over: Partial<MyHold> = {}): MyHold => ({
  holdId: 'h1', classId: 'c1', className: cls.name, status: 'HELD', expiresAt: new Date(START + 3000).toISOString(), ...over,
})

async function flush(ms = 0) {
  await act(async () => { await vi.advanceTimersByTimeAsync(ms) })
}

function renderDashboard() {
  window.localStorage.setItem('studentId', STUDENT)
  return render(<MemoryRouter initialEntries={['/dashboard']}><App /></MemoryRouter>)
}

beforeEach(() => {
  vi.resetAllMocks()
  resetServerClock()
  vi.useFakeTimers({ now: START })
})
afterEach(() => vi.useRealTimers())

describe('dashboard: automatic updates', () => {
  it('counts down, flips to EXPIRED by itself, disables payment and re-checks with the server', async () => {
    vi.mocked(api.getClasses).mockResolvedValue([cls])
    // first reads report HELD; once the server is asked again after expiry it reports EXPIRED
    vi.mocked(api.getMyHolds).mockResolvedValueOnce([hold()]).mockResolvedValue([hold({ status: 'EXPIRED' })])
    renderDashboard()
    await flush()

    expect(screen.getByRole('timer')).toHaveTextContent('00:03')
    expect(screen.getByRole('button', { name: /pay admission fee/i })).toBeEnabled()

    await flush(1000)
    expect(screen.getByRole('timer')).toHaveTextContent('00:02')

    const callsBeforeExpiry = vi.mocked(api.getMyHolds).mock.calls.length
    await flush(2000) // reaches expiresAt (4 s total > 3 s)
    const card = within(screen.getByRole('list', { name: /my applications/i }))
    expect(card.getByText('EXPIRED')).toBeInTheDocument()
    expect(screen.queryByRole('timer')).toBeNull()
    expect(card.getByRole('button', { name: /pay admission fee/i })).toBeDisabled()
    expect(card.getByText(/your reservation has expired/i)).toBeInTheDocument()
    expect(vi.mocked(api.getMyHolds).mock.calls.length).toBeGreaterThan(callsBeforeExpiry) // asked the server
  })

  it('polls classes and applications every 5 seconds and stops after unmount', async () => {
    vi.mocked(api.getClasses).mockResolvedValue([cls])
    vi.mocked(api.getMyHolds).mockResolvedValue([])
    const { unmount } = renderDashboard()
    await flush()
    const classCalls = () => vi.mocked(api.getClasses).mock.calls.length
    const holdCalls = () => vi.mocked(api.getMyHolds).mock.calls.length
    expect([classCalls(), holdCalls()]).toEqual([1, 1])

    await flush(5000)
    expect([classCalls(), holdCalls()]).toEqual([2, 2])
    await flush(5000)
    expect([classCalls(), holdCalls()]).toEqual([3, 3])

    unmount()
    await flush(30_000)
    expect([classCalls(), holdCalls()]).toEqual([3, 3])
    expect(vi.getTimerCount()).toBe(0)
  })

  it('picks up seat changes made by someone else on the next poll', async () => {
    vi.mocked(api.getMyHolds).mockResolvedValue([])
    vi.mocked(api.getClasses)
      .mockResolvedValueOnce([{ ...cls, heldSeats: 0, availableSeats: 1 }])
      .mockResolvedValue([cls])
    renderDashboard()
    await flush()
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled()
    await flush(5000)
    expect(screen.getByRole('button', { name: 'Fully booked' })).toBeDisabled()
  })
})

describe('dashboard: loading and failure states', () => {
  it('shows skeletons while loading', async () => {
    vi.mocked(api.getClasses).mockReturnValue(new Promise(() => {}))
    vi.mocked(api.getMyHolds).mockReturnValue(new Promise(() => {}))
    renderDashboard()
    await flush()
    expect(screen.getAllByRole('status', { name: /loading/i }).length).toBeGreaterThan(0)
  })

  it('shows a friendly banner when the server is unreachable, and recovers on the next poll', async () => {
    vi.mocked(api.getMyHolds).mockResolvedValue([])
    vi.mocked(api.getClasses)
      .mockRejectedValueOnce(Object.assign(new Error('Network Error'), { isAxiosError: true }))
      .mockResolvedValue([{ ...cls, heldSeats: 0, availableSeats: 1 }])
    renderDashboard()
    await flush()
    expect(screen.getByRole('alert')).toHaveTextContent('Unable to reach the server. Please check your connection and try again.')

    await flush(5000)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByRole('article', { name: cls.name })).toBeInTheDocument()
  })

  it('never shows raw server errors', async () => {
    vi.mocked(api.getMyHolds).mockResolvedValue([])
    vi.mocked(api.getClasses).mockRejectedValue(axiosError(500, 'INTERNAL_ERROR', 'System.Exception: stack trace'))
    renderDashboard()
    await flush()
    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('Something went wrong. Please try again.')
    expect(alert).not.toHaveTextContent('stack trace')
  })
})
