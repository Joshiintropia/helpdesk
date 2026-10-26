import { Link } from 'react-router'
import { useMyTickets } from '../api/hooks'
import { ErrorText, StatusBadge } from '../components/ui'
import { formatDate } from '../format'

export default function MyRequestsPage() {
  const { data: tickets, isLoading, error } = useMyTickets()

  return (
    <section>
      <h1 className="mb-6 text-2xl font-semibold">Mis solicitudes</h1>
      {isLoading && <p className="text-slate-500">Cargando…</p>}
      <ErrorText error={error} />
      {tickets?.length === 0 && (
        <p className="text-slate-600">
          Aún no tienes solicitudes. <Link to="/nueva" className="text-brand-700 underline">Levanta una</Link>.
        </p>
      )}
      <ul className="space-y-3">
        {tickets?.map((t) => (
          <li key={t.id}>
            <Link
              to={`/solicitudes/${t.id}`}
              className="flex flex-wrap items-center gap-3 rounded-xl border border-slate-200 bg-white p-4 hover:border-brand-600"
            >
              <span className="text-slate-500">#{t.id}</span>
              <span className="flex-1 font-medium">{t.title}</span>
              <StatusBadge status={t.status} />
              <span className="w-full text-sm text-slate-500 sm:w-auto">Actualizada {formatDate(t.updatedAt)}</span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}
