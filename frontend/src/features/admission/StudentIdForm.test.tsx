import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { StudentIdForm } from './StudentIdForm'

const MESSAGE = 'Please enter a valid 17-digit birth registration number.'

describe('StudentIdForm', () => {
  it.each(['123', '2015269123456789a', '201526912345678901', ''])('rejects %j with a clear message', async (value) => {
    const onSubmit = vi.fn()
    const user = userEvent.setup()
    render(<StudentIdForm onSubmit={onSubmit} />)
    if (value) await user.type(screen.getByLabelText(/birth registration number/i), value)
    await user.click(screen.getByRole('button', { name: /continue/i }))
    expect(await screen.findByRole('alert')).toHaveTextContent(MESSAGE)
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('submits a valid 17-digit id', async () => {
    const onSubmit = vi.fn()
    const user = userEvent.setup()
    render(<StudentIdForm onSubmit={onSubmit} />)
    await user.type(screen.getByLabelText(/birth registration number/i), '20152691234567890')
    await user.click(screen.getByRole('button', { name: /continue/i }))
    await waitFor(() => expect(onSubmit).toHaveBeenCalledWith('20152691234567890'))
    expect(screen.queryByRole('alert')).toBeNull()
  })
})
