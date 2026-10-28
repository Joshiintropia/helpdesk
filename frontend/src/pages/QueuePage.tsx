import { Link } from 'react-router'
import { useMe, useQueue } from '../api/hooks'
import { ErrorText, PriorityBadge, SlaBadge, StatusBadge } from '../components/ui'
import { formatDate } from '../format'

/** What each of my areas has to work on now, the closest SLA first. Escalated items show why they arrived. */
export default function QueuePage() {
  const { data: me } = useMe()
  const { data: items, isLoading, error } = useQueue()
  const areaName = (id: number) => me?.areas.find((a) => a.id === id)?.name ?? `Área ${id}`

  return (
    <section>
      <h1 className="mb-6 text-2xl font-semibold">Bandeja</h1>
      {isLoading && <p className="text-slate-500">Cargando…</p>}
      <ErrorText error={error} />
      {items?.length === 0 && <p className="text-slate-600">No hay nada pendiente.</p>}
      <ul className="space-y-3">
        {items?.map((item) => (
          <li key={item.segmentId}>
            <Link
              to={`/solicitudes/${item.ticketId}`}
              className="block rounded-xl border border-slate-200 bg-white p-4 hover:border-brand-600"
            >
              <div className="flex flex-wrap items-center gap-3">
                <span className="text-slate-500">#{item.ticketId}</span>
                <span className="flex-1 font-medium">{item.title}</span>
                <PriorityBadge priority={item.priority} />
                <StatusBadge status={item.status} />
              </div>
              <div className="mt-2">
                <SlaBadge sla={item.sla} />
              </div>
              <div className="mt-2 text-sm text-slate-600">
                {areaName(item.areaId)} · en esta área desde {formatDate(item.openedAt)}
                {item.escalationReason && <span className="block">Escalado: “{item.escalationReason}”</span>}
              </div>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}
