import { Route, Routes } from 'react-router-dom'

function Placeholder({ title }: { title: string }) {
  return (
    <main className="mx-auto max-w-3xl p-6">
      <h1 className="text-2xl font-semibold text-slate-900">{title}</h1>
    </main>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Placeholder title="School Admission Portal" />} />
      <Route path="/dashboard" element={<Placeholder title="Parent Dashboard" />} />
      <Route path="/admin" element={<Placeholder title="Admin Dashboard" />} />
    </Routes>
  )
}
