import axios from 'axios'
import { setServerTime } from '../utils/serverClock'

export const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
  timeout: 15_000,
})

// The Date header is truncated to whole seconds, so the true server time lies in [d, d + 1s).
// Using the midpoint halves the worst-case error.
const DATE_HEADER_MIDPOINT_MS = 500

function syncClock(headers: unknown): void {
  const date = (headers as { date?: string } | undefined)?.date
  if (date) setServerTime(Date.parse(date) + DATE_HEADER_MIDPOINT_MS)
}

http.interceptors.response.use(
  (response) => {
    syncClock(response.headers)
    return response
  },
  (error) => {
    if (axios.isAxiosError(error)) syncClock(error.response?.headers)
    return Promise.reject(error)
  },
)
