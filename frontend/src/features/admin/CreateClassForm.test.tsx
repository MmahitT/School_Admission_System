import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../api/admission'
import { axiosError, renderWithProviders } from '../../test/render'
import { CreateClassForm } from './CreateClassForm'

vi.mock('../../api/admission')
beforeEach(() => vi.resetAllMocks())

async function fill(name: string, seats: string) {
  const user = userEvent.setup()
  if (name) await user.type(screen.getByLabelText(/class \/ shift name/i), name)
  if (seats) await user.type(screen.getByLabelText(/total seats/i), seats)
  await user.click(screen.getByRole('button', { name: /create class/i }))
}

describe('admin class creation', () => {
  it('creates a class, confirms to the admin, clears the form and refreshes the list', async () => {
    vi.mocked(api.createClass).mockResolvedValue({ id: 'c1', name: 'Class 6 - Morning Shift', totalSeats: 120 })
    const onCreated = vi.fn()
    renderWithProviders(<CreateClassForm onCreated={onCreated} />)
    await fill('  Class 6 - Morning Shift ', '120')

    await waitFor(() => expect(api.createClass).toHaveBeenCalledWith('Class 6 - Morning Shift', 120))
    expect(await screen.findByText(/created with 120 seats/i)).toBeInTheDocument()
    expect(onCreated).toHaveBeenCalled()
    expect(screen.getByLabelText(/class \/ shift name/i)).toHaveValue('')
  })

  it.each([
    ['', '10', 'Class name is required.'],
    ['Class 6', '', 'Enter a whole number of seats.'],
    ['Class 6', 'abc', 'Enter a whole number of seats.'],
    ['Class 6', '0', 'Total seats must be greater than zero.'],
    ['Class 6', '1.5', 'Enter a whole number of seats.'],
  ])('validates name=%j seats=%j', async (name, seats, message) => {
    renderWithProviders(<CreateClassForm onCreated={vi.fn()} />)
    await fill(name, seats)
    expect(await screen.findByText(message)).toBeInTheDocument()
    expect(api.createClass).not.toHaveBeenCalled()
  })

  it('shows a friendly message when the server rejects a duplicate name', async () => {
    vi.mocked(api.createClass).mockRejectedValue(axiosError(409, 'DUPLICATE_CLASS_NAME'))
    renderWithProviders(<CreateClassForm onCreated={vi.fn()} />)
    await fill('Class 6', '10')
    expect(await screen.findByRole('alert')).toHaveTextContent('A class with this name already exists.')
  })
})
