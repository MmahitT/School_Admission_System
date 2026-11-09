import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../api/admission'
import { axiosError, renderWithProviders, STUDENT } from '../../test/render'
import type { AdmissionClass, MyHold } from '../../types/api'
import { ClassList } from './ClassList'

vi.mock('../../api/admission')

const NOW = Date.parse('2026-11-01T04:00:00Z')
const cls = (over: Partial<AdmissionClass>): AdmissionClass => ({
  id: 'c1', name: 'Class 6 - Morning Shift', totalSeats: 120, heldSeats: 15, confirmedSeats: 80, availableSeats: 25, ...over,
})
const hold = (over: Partial<MyHold>): MyHold => ({
  holdId: 'h1', classId: 'c1', className: 'Class 6 - Morning Shift', status: 'HELD', expiresAt: '2026-11-01T04:10:00Z', ...over,
})

function setup(classes: AdmissionClass[], holds: MyHold[] = [], onChanged = vi.fn()) {
  renderWithProviders(<ClassList classes={classes} holds={holds} studentId={STUDENT} now={NOW} onChanged={onChanged} />)
  return onChanged
}

beforeEach(() => vi.resetAllMocks())

describe('class listing', () => {
  it('shows every class with its seat numbers', () => {
    setup([cls({}), cls({ id: 'c2', name: 'Class 7 - Day Shift', totalSeats: 50, heldSeats: 0, confirmedSeats: 0, availableSeats: 50 })])
    const card = screen.getByRole('article', { name: 'Class 6 - Morning Shift' })
    expect(within(card).getByText('25')).toBeInTheDocument()
    expect(within(card).getByText(/of 120 seats available/)).toBeInTheDocument()
    expect(within(card).getByText('Available')).toBeInTheDocument()
    expect(screen.getByRole('article', { name: 'Class 7 - Day Shift' })).toBeInTheDocument()
  })

  it('labels limited and fully booked classes and disables Apply when full', () => {
    setup([cls({ id: 'a', name: 'Almost', availableSeats: 3 }), cls({ id: 'f', name: 'Full', availableSeats: 0, heldSeats: 40, confirmedSeats: 80 })])
    const limited = screen.getByRole('article', { name: 'Almost' })
    expect(within(limited).getByText('Limited seats')).toBeInTheDocument()
    expect(within(limited).getByRole('button', { name: 'Apply' })).toBeEnabled()
    const full = screen.getByRole('article', { name: 'Full' })
    expect(within(full).getByText('Fully booked', { selector: 'span' })).toBeInTheDocument()
    expect(within(full).getByRole('button', { name: 'Fully booked' })).toBeDisabled()
  })

  it('shows an empty state', () => {
    setup([])
    expect(screen.getByText(/no classes are open/i)).toBeInTheDocument()
  })

  it('disables Apply elsewhere while the student has an active hold, and shows the application status', () => {
    setup([cls({}), cls({ id: 'c2', name: 'Other' })], [hold({})])
    expect(within(screen.getByRole('article', { name: 'Other' })).getByRole('button')).toBeDisabled()
    expect(within(screen.getByRole('article', { name: 'Class 6 - Morning Shift' })).getByText('HELD')).toBeInTheDocument()
  })

  it('allows applying again to a class where the previous hold expired', () => {
    setup([cls({})], [hold({ expiresAt: '2026-11-01T03:59:00Z' })]) // past expiry => EXPIRED
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled()
  })
})

describe('applying', () => {
  it('asks for confirmation, calls the API with the student id, then refreshes', async () => {
    vi.mocked(api.createHold).mockResolvedValue({ holdId: 'h9', classId: 'c1', studentId: STUDENT, status: 'HELD', expiresAt: '2026-11-01T04:10:00Z' })
    const onChanged = setup([cls({})])
    const user = userEvent.setup()

    await user.click(screen.getByRole('button', { name: 'Apply' }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByText(/apply for admission/i)).toBeInTheDocument()
    expect(api.createHold).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: 'Confirm application' }))
    await waitFor(() => expect(api.createHold).toHaveBeenCalledWith('c1', STUDENT))
    expect(await screen.findByText(/seat reserved in class 6/i)).toBeInTheDocument()
    expect(onChanged).toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('cancelling the dialog sends nothing', async () => {
    setup([cls({})])
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }))
    expect(api.createHold).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it.each([
    ['NO_SEATS_AVAILABLE', 'No seats are currently available for this class.'],
    ['ACTIVE_HOLD_EXISTS', 'You already have an active admission reservation.'],
  ])('shows a friendly message for %s and still refreshes', async (code, message) => {
    vi.mocked(api.createHold).mockRejectedValue(axiosError(409, code))
    const onChanged = setup([cls({})])
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    await user.click(screen.getByRole('button', { name: 'Confirm application' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(message)
    expect(onChanged).toHaveBeenCalled()
  })

  it('shows the generic message for server errors', async () => {
    vi.mocked(api.createHold).mockRejectedValue(axiosError(500, 'INTERNAL_ERROR', 'Npgsql stack trace'))
    setup([cls({})])
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    await user.click(screen.getByRole('button', { name: 'Confirm application' }))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Something went wrong. Please try again.')
    expect(alert).not.toHaveTextContent('Npgsql')
  })
})
