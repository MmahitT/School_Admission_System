import { Navigate, Route, Routes } from 'react-router-dom'
import { ToastProvider } from './components/Toast'
import { StudentProvider } from './hooks/StudentProvider'
import { AppLayout } from './layouts/AppLayout'
import AdminPage from './pages/AdminPage'
import DashboardPage from './pages/DashboardPage'
import LandingPage from './pages/LandingPage'

export default function App() {
  return (
    <StudentProvider>
      <ToastProvider>
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/" element={<LandingPage />} />
            <Route path="/dashboard" element={<DashboardPage />} />
            <Route path="/admin" element={<AdminPage />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      </ToastProvider>
    </StudentProvider>
  )
}
