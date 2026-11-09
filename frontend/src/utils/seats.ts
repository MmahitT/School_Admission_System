export type SeatState = 'available' | 'limited' | 'full'

/** Limited = at most 5 seats or at most 10% of capacity left (ASSUMPTIONS A17). */
export function seatState(availableSeats: number, totalSeats: number): SeatState {
  if (availableSeats <= 0) return 'full'
  if (availableSeats <= 5 || availableSeats / totalSeats <= 0.1) return 'limited'
  return 'available'
}

export function utilizationPercent(held: number, confirmed: number, total: number): number {
  if (total <= 0) return 0
  return Math.min(100, Math.round(((held + confirmed) / total) * 100))
}
