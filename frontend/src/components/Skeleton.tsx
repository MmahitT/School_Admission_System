export function Skeleton({ className = '' }: { className?: string }) {
  return <div aria-hidden="true" className={`animate-pulse rounded bg-slate-200 ${className}`} />
}

export function CardSkeletons({ count = 3 }: { count?: number }) {
  return (
    <div role="status" aria-label="Loading" className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="space-y-3 rounded-xl border border-slate-200 bg-white p-5">
          <Skeleton className="h-5 w-3/4" />
          <Skeleton className="h-3 w-full" />
          <Skeleton className="h-3 w-1/2" />
          <Skeleton className="h-9 w-full" />
        </div>
      ))}
    </div>
  )
}
