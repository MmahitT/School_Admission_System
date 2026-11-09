import { act, render, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { resetServerClock } from '../utils/serverClock'
import { usePolledResource } from './usePolledResource'
import { useServerNow } from './useServerNow'

beforeEach(() => { vi.useFakeTimers({ now: 1_000_000 }); resetServerClock() })
afterEach(() => vi.useRealTimers())

describe('usePolledResource', () => {
  it('fetches immediately, polls on the interval, and stops after unmount', async () => {
    const fetcher = vi.fn().mockResolvedValue('data')
    const { result, unmount } = renderHook(() => usePolledResource(fetcher, 5000))
    await act(async () => {})
    expect(fetcher).toHaveBeenCalledTimes(1)
    expect(result.current.data).toBe('data')

    await act(async () => { await vi.advanceTimersByTimeAsync(5000) })
    expect(fetcher).toHaveBeenCalledTimes(2)

    unmount()
    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })
    expect(fetcher).toHaveBeenCalledTimes(2)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('keeps previous data and exposes the error when a poll fails', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce('ok').mockRejectedValueOnce(new Error('down'))
    const { result } = renderHook(() => usePolledResource(fetcher, 1000))
    await act(async () => {})
    await act(async () => { await vi.advanceTimersByTimeAsync(1000) })
    expect(result.current.data).toBe('ok')
    expect(result.current.error).toBeInstanceOf(Error)
  })

  it('does not poll while the tab is hidden', async () => {
    const fetcher = vi.fn().mockResolvedValue(1)
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => true })
    renderHook(() => usePolledResource(fetcher, 1000))
    await act(async () => {})
    const callsAfterMount = fetcher.mock.calls.length
    await act(async () => { await vi.advanceTimersByTimeAsync(5000) })
    expect(fetcher.mock.calls.length).toBe(callsAfterMount)
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => false })
  })
})

describe('useServerNow', () => {
  it('ticks every second and clears its timer on unmount', () => {
    function Probe() { return <span data-testid="t">{useServerNow(1000)}</span> }
    const { getByTestId, unmount } = render(<Probe />)
    const first = Number(getByTestId('t').textContent)
    act(() => { vi.advanceTimersByTime(3000) })
    expect(Number(getByTestId('t').textContent)).toBe(first + 3000)
    unmount()
    expect(vi.getTimerCount()).toBe(0)
  })
})
