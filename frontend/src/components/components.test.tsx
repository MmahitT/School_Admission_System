import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ConfirmDialog } from './ConfirmDialog'
import { SeatMeter } from './SeatMeter'
import { StatusBadge } from './StatusBadge'
import { ToastProvider } from './Toast'
import { useToast } from './useToast'

afterEach(() => vi.useRealTimers())

function dialog(over: Partial<React.ComponentProps<typeof ConfirmDialog>> = {}) {
  const onConfirm = vi.fn(), onCancel = vi.fn()
  render(
    <ConfirmDialog open title="Sure?" confirmLabel="Do it" onConfirm={onConfirm} onCancel={onCancel} {...over}>
      <p>Body text</p>
    </ConfirmDialog>,
  )
  return { onConfirm, onCancel }
}

describe('ConfirmDialog', () => {
  it('renders nothing when closed', () => {
    render(<ConfirmDialog open={false} title="x" confirmLabel="y" onConfirm={vi.fn()} onCancel={vi.fn()}>z</ConfirmDialog>)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('is an accessible modal, focuses the primary action, and confirms on click', async () => {
    const { onConfirm } = dialog()
    const d = screen.getByRole('dialog', { name: 'Sure?' })
    expect(d).toHaveAttribute('aria-modal', 'true')
    expect(screen.getByRole('button', { name: 'Do it' })).toHaveFocus()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Do it' }))
    expect(onConfirm).toHaveBeenCalledTimes(1)
  })

  it('cancels with Escape, the Cancel button, and a backdrop click', async () => {
    const { onCancel } = dialog()
    const user = userEvent.setup()
    await user.keyboard('{Escape}')
    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    await user.pointer({ keys: '[MouseLeft]', target: screen.getByRole('dialog').parentElement! })
    expect(onCancel).toHaveBeenCalledTimes(3)
  })

  it('ignores Escape/backdrop and disables both buttons while busy', async () => {
    const { onCancel, onConfirm } = dialog({ busy: true })
    const user = userEvent.setup()
    await user.keyboard('{Escape}')
    await user.pointer({ keys: '[MouseLeft]', target: screen.getByRole('dialog').parentElement! })
    expect(screen.getByRole('button', { name: 'Do it' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled()
    expect(onCancel).not.toHaveBeenCalled()
    expect(onConfirm).not.toHaveBeenCalled()
  })
})

function ToastButtons() {
  const toast = useToast()
  return (
    <>
      <button onClick={() => toast.success('Saved!')}>ok</button>
      <button onClick={() => toast.error('Failed!')}>bad</button>
    </>
  )
}

describe('Toasts', () => {
  it('announces success as status and errors as alerts, then auto-dismisses and leaves no timers', () => {
    vi.useFakeTimers()
    const { unmount } = render(<ToastProvider><ToastButtons /></ToastProvider>)
    act(() => { screen.getByText('ok').click(); screen.getByText('bad').click() })
    expect(screen.getByRole('status')).toHaveTextContent('Saved!')
    expect(screen.getByRole('alert')).toHaveTextContent('Failed!')

    act(() => { vi.advanceTimersByTime(6100) })
    expect(screen.queryByRole('status')).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
    unmount()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('can be dismissed manually and clears pending timers on unmount', () => {
    vi.useFakeTimers()
    const { unmount } = render(<ToastProvider><ToastButtons /></ToastProvider>)
    act(() => { screen.getByText('bad').click() })
    act(() => { screen.getByRole('button', { name: /dismiss/i }).click() })
    expect(screen.queryByRole('alert')).toBeNull()
    act(() => { screen.getByText('ok').click() })
    unmount()
    expect(vi.getTimerCount()).toBe(0)
  })
})

describe('StatusBadge and SeatMeter', () => {
  it.each([['HELD'], ['CONFIRMED'], ['EXPIRED'], ['CANCELLED']] as const)('shows %s as text, not only colour', (status) => {
    render(<StatusBadge status={status} />)
    expect(screen.getByText(status)).toBeInTheDocument()
  })

  it('SeatMeter exposes utilization to assistive tech and never exceeds 100%', () => {
    const { rerender } = render(<SeatMeter held={10} confirmed={20} total={120} />)
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '25')
    rerender(<SeatMeter held={500} confirmed={500} total={100} />)
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '100')
  })
})
