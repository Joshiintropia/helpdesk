import { NavLink, Navigate, Outlet, Route, Routes } from 'react-router'
import { useLogout, useMe } from './api/hooks'
import LoginPage from './pages/LoginPage'
import MyRequestsPage from './pages/MyRequestsPage'
import NewRequestPage from './pages/NewRequestPage'
import QueuePage from './pages/QueuePage'
import TicketPage from './pages/TicketPage'

export default function App() {
  return (
    <Routes>
      <Route path="/entrar" element={<LoginPage />} />
      <Route element={<SignedIn />}>
        <Route index element={<Navigate to="/nueva" replace />} />
        <Route path="/nueva" element={<NewRequestPage />} />
        <Route path="/mis-solicitudes" element={<MyRequestsPage />} />
        <Route path="/solicitudes/:id" element={<TicketPage />} />
        <Route path="/bandeja" element={<QueuePage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

function SignedIn() {
  const { data: me, isLoading } = useMe()
  const logout = useLogout()

  if (isLoading) return <p className="p-8 text-slate-500">Cargando…</p>
  if (!me) return <Navigate to="/entrar" replace />

  const isAgent = me.areas.length > 0 || me.roles.some((r) => r === 'Admin' || r === 'Supervisor')
  const link = ({ isActive }: { isActive: boolean }) =>
    `rounded-lg px-3 py-2 font-medium ${isActive ? 'bg-brand-50 text-brand-700' : 'text-slate-600 hover:text-slate-900'}`

  return (
    <div className="min-h-screen">
      <header className="border-b border-slate-200 bg-white">
        <nav className="mx-auto flex max-w-4xl flex-wrap items-center gap-2 px-4 py-3">
          <span className="mr-4 text-lg font-semibold">Mesa de ayuda</span>
          <NavLink to="/nueva" className={link}>
            Nueva solicitud
          </NavLink>
          <NavLink to="/mis-solicitudes" className={link}>
            Mis solicitudes
          </NavLink>
          {isAgent && (
            <NavLink to="/bandeja" className={link}>
              Bandeja
            </NavLink>
          )}
          <span className="ml-auto text-sm text-slate-500">{me.displayName || me.email}</span>
          <button className="text-sm text-slate-600 underline" onClick={() => logout.mutate()}>
            Salir
          </button>
        </nav>
      </header>
      <main className="mx-auto max-w-4xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}
