import { render } from '@testing-library/react'
import type { ReactElement, ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { ToastProvider } from '../components/Toast'
import { StudentProvider } from '../hooks/StudentProvider'

export function renderWithProviders(ui: ReactElement, route = '/') {
  // `wrapper` (not wrapping the element) so that rerender() keeps the providers.
  const Wrapper = ({ children }: { children: ReactNode }) => (
    <MemoryRouter initialEntries={[route]}>
      <StudentProvider>
        <ToastProvider>{children}</ToastProvider>
      </StudentProvider>
    </MemoryRouter>
  )
  return render(ui, { wrapper: Wrapper })
}

export const STUDENT = '20152691234567890'

export function axiosError(status: number, code?: string, detail = 'server detail') {
  return Object.assign(new Error('Request failed'), {
    isAxiosError: true,
    response: { status, data: { status, code, detail }, headers: {} },
  })
}
