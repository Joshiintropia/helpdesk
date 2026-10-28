import { NavLink, Navigate, Outlet, Route, Routes } from 'react-router'
import { useLogout, useMe } from './api/hooks'
import AreasPage from './pages/AreasPage'
import ChangePasswordPage from './pages/ChangePasswordPage'
import LoginPage from './pages/LoginPage'
import MyRequestsPage from './pages/MyRequestsPage'
import NewRequestPage from './pages/NewRequestPage'
import QueuePage from './pages/QueuePage'
import SlaPage from './pages/SlaPage'
import TicketPage from './pages/TicketPage'
import UsersPage from './pages/UsersPage'

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
        <Route path="/admin/usuarios" element={<UsersPage />} />
        <Route path="/admin/areas" element={<AreasPage />} />
        <Route path="/admin/sla" element={<SlaPage />} />
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

  const isAdmin = me.roles.includes('Admin')
  const isAgent = me.areas.length > 0 || isAdmin || me.roles.includes('Supervisor')
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
          {isAgent && !me.mustChangePassword && (
            <NavLink to="/bandeja" className={link}>
              Bandeja
            </NavLink>
          )}
          {isAdmin && !me.mustChangePassword && (
            <>
              <NavLink to="/admin/usuarios" className={link}>
                Usuarios
              </NavLink>
              <NavLink to="/admin/areas" className={link}>
                Áreas
              </NavLink>
              <NavLink to="/admin/sla" className={link}>
                SLA
              </NavLink>
            </>
          )}
          <span className="ml-auto text-sm text-slate-500">{me.displayName || me.email}</span>
          <button className="text-sm text-slate-600 underline" onClick={() => logout.mutate()}>
            Salir
          </button>
        </nav>
      </header>
      <main className="mx-auto max-w-4xl px-4 py-8">
        {me.mustChangePassword ? <ChangePasswordPage /> : <Outlet />}
      </main>
    </div>
  )
}
