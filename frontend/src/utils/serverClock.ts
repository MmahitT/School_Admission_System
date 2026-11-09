// The backend is the source of truth for time. We learn the offset between the server clock and the
// browser clock from the HTTP `Date` header of every API response and apply it to countdowns.
let offsetMs = 0

export function setServerTime(serverEpochMs: number, localEpochMs: number = Date.now()): void {
  if (Number.isFinite(serverEpochMs)) offsetMs = serverEpochMs - localEpochMs
}

export function serverNow(): number {
  return Date.now() + offsetMs
}

export function resetServerClock(): void {
  offsetMs = 0
}
