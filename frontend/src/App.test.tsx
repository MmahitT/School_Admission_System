import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from './api/admission'
import App from './App'
import { STUDENT } from './test/render'

vi.mock('./api/admission')

const classes = [{ id: 'c1', name: 'Class 6 - Morning Shift', totalSeats: 120, heldSeats: 15, confirmedSeats: 80, availableSeats: 25 }]
const holds = [{ holdId: 'h1', classId: 'c1', className: 'Class 6 - Morning Shift', status: 'CONFIRMED' as const, expiresAt: '2026-11-01T04:10:00Z' }]

function renderApp(route: string) {
  return render(<MemoryRouter initialEntries={[route]}><App /></MemoryRouter>)
}

beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(api.getClasses).mockResolvedValue(classes)
  vi.mocked(api.getMyHolds).mockResolvedValue(holds)
})

describe('student identification and persistence', () => {
  it('landing page shows the portal, saves a valid id and opens the dashboard with data from the API', async () => {
    const user = userEvent.setup()
    renderApp('/')
    expect(screen.getByRole('heading', { name: /admission portal/i })).toBeInTheDocument()

    await user.type(screen.getByLabelText(/birth registration number/i), STUDENT)
    await user.click(screen.getByRole('button', { name: /continue/i }))

    expect(await screen.findByRole('heading', { name: /admission dashboard/i })).toBeInTheDocument()
    expect(window.localStorage.getItem('studentId')).toBe(STUDENT)
    expect(api.getMyHolds).toHaveBeenCalledWith(STUDENT)
    expect(await screen.findByRole('article', { name: 'Class 6 - Morning Shift' })).toBeInTheDocument()
    const mine = within(screen.getByRole('list', { name: /my applications/i }))
    expect(mine.getByText('CONFIRMED')).toBeInTheDocument()
  })

  it('restores the saved student after a refresh, loading applications from the server (not localStorage)', async () => {
    window.localStorage.setItem('studentId', STUDENT)
    renderApp('/dashboard')
    expect(await screen.findByRole('list', { name: /my applications/i })).toBeInTheDocument()
    expect(screen.getAllByText(STUDENT).length).toBeGreaterThan(0)
    expect(api.getMyHolds).toHaveBeenCalledWith(STUDENT)
    expect(Object.keys(window.localStorage)).toEqual(['studentId']) // only the id is stored locally
  })

  it('landing page pre-fills the saved id ("welcome back")', () => {
    window.localStorage.setItem('studentId', STUDENT)
    renderApp('/')
    expect(screen.getByLabelText(/birth registration number/i)).toHaveValue(STUDENT)
    expect(screen.getByText(/welcome back/i)).toBeInTheDocument()
  })

  it('redirects to the landing page when no student is saved', async () => {
    renderApp('/dashboard')
    expect(await screen.findByLabelText(/birth registration number/i)).toBeInTheDocument()
    expect(api.getClasses).not.toHaveBeenCalled()
  })

  it('"Change" forgets the student', async () => {
    window.localStorage.setItem('studentId', STUDENT)
    const user = userEvent.setup()
    renderApp('/dashboard')
    await user.click(await screen.findByRole('button', { name: /change/i }))
    expect(window.localStorage.getItem('studentId')).toBeNull()
    expect(await screen.findByLabelText(/birth registration number/i)).toHaveValue('')
  })
})

describe('admin interface', () => {
  it('lists classes with seat figures and utilization', async () => {
    renderApp('/admin')
    const row = (await screen.findByRole('row', { name: /class 6 - morning shift/i }))
    expect(within(row).getByText('120')).toBeInTheDocument()
    expect(within(row).getByText('15')).toBeInTheDocument()
    expect(within(row).getByText('80')).toBeInTheDocument()
    expect(within(row).getByText('25')).toBeInTheDocument()
    expect(within(row).getByText('79%')).toBeInTheDocument()
    expect(screen.getByLabelText(/class \/ shift name/i)).toBeInTheDocument()
  })
})
