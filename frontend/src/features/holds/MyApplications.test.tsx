import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../api/admission'
import { axiosError, renderWithProviders, STUDENT } from '../../test/render'
import type { MyHold } from '../../types/api'
import { MyApplications } from './MyApplications'

vi.mock('../../api/admission')

const EXPIRES = '2026-11-01T04:10:00Z'
const T0 = Date.parse('2026-11-01T04:00:00Z') // 10:00 before expiry
const hold = (over: Partial<MyHold> = {}): MyHold => ({
  holdId: 'h1', classId: 'c1', className: 'Class 6 - Morning Shift', status: 'HELD', expiresAt: EXPIRES, ...over,
})

function setup(holds: MyHold[], now = T0, onChanged = vi.fn()) {
  const ui = (n: number) => <MyApplications studentId={STUDENT} holds={holds} loading={false} now={n} onChanged={onChanged} />
  const view = renderWithProviders(ui(now))
  return { onChanged, view, ui }
}

beforeEach(() => vi.resetAllMocks())

describe('countdown', () => {
  it('shows the time remaining until the server expiresAt', () => {
    setup([hold()])
    expect(screen.getByRole('timer')).toHaveTextContent('10:00')
    expect(screen.getByText('HELD')).toBeInTheDocument()
  })

  it('counts down as server time advances', () => {
    const { view, ui } = setup([hold()])
    view.rerender(ui(T0 + 61_000))
    expect(screen.getByRole('timer')).toHaveTextContent('08:59')
  })
})

describe('expiration', () => {
  it('flips to EXPIRED at the deadline: no timer, payment disabled, message shown, server asked to confirm', async () => {
    const { onChanged } = setup([hold()], Date.parse(EXPIRES))
    expect(screen.getByText('EXPIRED')).toBeInTheDocument()
    expect(screen.queryByRole('timer')).toBeNull()
    expect(screen.getByRole('button', { name: /pay admission fee/i })).toBeDisabled()
    expect(screen.queryByRole('button', { name: /cancel reservation/i })).toBeNull()
    expect(screen.getByText(/your reservation has expired/i)).toBeInTheDocument()
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
  })

  it('shows server-reported EXPIRED, CANCELLED and CONFIRMED distinctly with no pay button for the last two', () => {
    setup([
      hold({ holdId: 'a', status: 'EXPIRED' }),
      hold({ holdId: 'b', status: 'CANCELLED' }),
      hold({ holdId: 'c', status: 'CONFIRMED' }),
    ])
    expect(screen.getByText('EXPIRED')).toBeInTheDocument()
    expect(screen.getByText('CANCELLED')).toBeInTheDocument()
    expect(screen.getByText('CONFIRMED')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /pay admission fee/i })).toHaveLength(1) // only the disabled one for EXPIRED
    expect(screen.getByText(/admission confirmed\. congratulations/i)).toBeInTheDocument()
  })

  it('shows an empty state when there are no applications', () => {
    setup([])
    expect(screen.getByText(/have not applied/i)).toBeInTheDocument()
  })
})

describe('simulated payment', () => {
  it('explains the payment is simulated, confirms via the API, and refreshes', async () => {
    vi.mocked(api.confirmHold).mockResolvedValue({ holdId: 'h1', status: 'CONFIRMED' })
    const { onChanged } = setup([hold()])
    const user = userEvent.setup()

    await user.click(screen.getByRole('button', { name: /pay admission fee/i }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByText(/simulated payment/i, { selector: 'h2' })).toBeInTheDocument()
    expect(within(dialog).getByText(/no real money is charged/i)).toBeInTheDocument()
    expect(api.confirmHold).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: /confirm simulated payment/i }))
    await waitFor(() => expect(api.confirmHold).toHaveBeenCalledWith('h1', STUDENT))
    expect(await screen.findByText(/payment successful \(simulated\)/i)).toBeInTheDocument()
    expect(onChanged).toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('prevents repeated clicks while the payment is processing', async () => {
    let resolve!: () => void
    vi.mocked(api.confirmHold).mockImplementation(() => new Promise((r) => { resolve = () => r({ holdId: 'h1', status: 'CONFIRMED' }) }))
    setup([hold()])
    const user = userEvent.setup()

    await user.click(screen.getByRole('button', { name: /pay admission fee/i }))
    const confirm = screen.getByRole('button', { name: /confirm simulated payment/i })
    await user.click(confirm)
    await user.click(confirm)
    await user.click(confirm)
    expect(confirm).toBeDisabled()
    expect(api.confirmHold).toHaveBeenCalledTimes(1)
    resolve()
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('shows the expired-reservation message if the server rejects the payment', async () => {
    vi.mocked(api.confirmHold).mockRejectedValue(axiosError(409, 'HOLD_EXPIRED'))
    const { onChanged } = setup([hold()])
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: /pay admission fee/i }))
    await user.click(screen.getByRole('button', { name: /confirm simulated payment/i }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Your reservation has expired. Please apply again if seats are available.')
    expect(onChanged).toHaveBeenCalled()
  })
})

describe('cancelling', () => {
  it('cancels through the API after confirmation', async () => {
    vi.mocked(api.cancelHold).mockResolvedValue(undefined)
    const { onChanged } = setup([hold()])
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: /cancel reservation/i }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: /yes, cancel reservation/i }))
    await waitFor(() => expect(api.cancelHold).toHaveBeenCalledWith('h1', STUDENT))
    expect(await screen.findByText(/was cancelled/i)).toBeInTheDocument()
    expect(onChanged).toHaveBeenCalled()
  })
})
