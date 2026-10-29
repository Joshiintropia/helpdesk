import { useState, type FormEvent } from 'react'
import { useParams } from 'react-router'
import { useAreas, useMe, useTicket, useTicketAction } from '../api/hooks'
import type { Me, MessageVisibility, Segment, TicketDetail, TicketPriority } from '../api/types'
import { AttachmentList, FilePicker } from '../components/attachments'
import { RatingForm, Stars } from '../components/Rating'
import { Button, Card, ErrorText, PriorityBadge, SlaBadge, StatusBadge } from '../components/ui'
import { usePendingFiles } from '../components/usePendingFiles'
import { formatDate, priorities, priorityLabel } from '../format'

export default function TicketPage() {
  const id = Number(useParams().id)
  const { data: me } = useMe()
  const { data: ticket, isLoading, error } = useTicket(id)
  const action = useTicketAction(id)

  if (isLoading) return <p className="text-slate-500">Cargando…</p>
  if (error || !ticket || !me) return <ErrorText error={error ?? new Error('No encontramos esta solicitud.')} />

  const isAgentView = ticket.segments !== null && ticket.requester.id !== me.id
  const finished = ticket.status === 'Resolved' || ticket.status === 'Closed'

  return (
    <div className="space-y-6">
      <Card>
        <div className="mb-3 flex flex-wrap items-center gap-3">
          <span className="text-slate-500">#{ticket.id}</span>
          <StatusBadge status={ticket.status} />
          {isAgentView && <PriorityBadge priority={ticket.priority} />}
          {isAgentView && <span className="text-sm text-slate-600">Turno de: {ticket.currentAreaName}</span>}
        </div>
        <h1 className="mb-2 text-2xl font-semibold">{ticket.title}</h1>
        {ticket.description !== ticket.title && <p className="whitespace-pre-wrap text-slate-700">{ticket.description}</p>}
        <AttachmentList attachments={ticket.attachments} />
        <p className="mt-3 text-sm text-slate-500">
          {ticket.requester.displayName} · {formatDate(ticket.createdAt)}
        </p>
        {ticket.status === 'Resolved' && ticket.requester.id === me.id && (
          <div className="mt-4 rounded-lg bg-emerald-50 p-4">
            <p className="mb-1 text-emerald-900">Marcamos tu solicitud como resuelta. ¿Quedó todo bien?</p>
            <p className="mb-3 text-sm text-emerald-800">Si no nos escribes, se cerrará sola en unos días.</p>
            <div className="flex flex-wrap gap-3">
              <Button variant="secondary" onClick={() => action.mutate({ path: '/close' })} disabled={action.isPending}>
                Sí, ya quedó
              </Button>
              <Button variant="secondary" onClick={() => action.mutate({ path: '/reopen' })} disabled={action.isPending}>
                No, sigue pasando
              </Button>
            </div>
            <ErrorText error={action.error} />
          </div>
        )}
        {ticket.status === 'Resolved' && isAgentView && (
          <div className="mt-4 flex flex-wrap items-center gap-3 rounded-lg bg-slate-50 p-4">
            <p className="flex-1 text-slate-700">Resuelta. Se cerrará sola si el solicitante no responde en unos días.</p>
            <Button variant="secondary" onClick={() => action.mutate({ path: '/close' })} disabled={action.isPending}>
              Cerrar ahora
            </Button>
            <ErrorText error={action.error} />
          </div>
        )}
      </Card>

      <SatisfactionCard ticket={ticket} isRequester={ticket.requester.id === me.id} />

      {isAgentView && <Chain ticket={ticket} me={me} />}

      <Conversation ticket={ticket} isAgentView={isAgentView} finished={finished} />
    </div>
  )
}

/** The survey: the requester rates once resolved (and may change it until closed); agents see the answer. */
function SatisfactionCard({ ticket, isRequester }: { ticket: TicketDetail; isRequester: boolean }) {
  const action = useTicketAction(ticket.id)
  const { satisfaction } = ticket
  const canRate = isRequester && (ticket.status === 'Resolved' || (ticket.status === 'Closed' && !satisfaction))

  if (canRate) {
    return (
      <Card>
        {satisfaction && <p className="mb-3 text-emerald-700">¡Gracias por tu calificación! Puedes cambiarla mientras la solicitud siga abierta.</p>}
        <RatingForm
          key={satisfaction?.ratedAt ?? 'new'}
          initialRating={satisfaction?.rating}
          initialComment={satisfaction?.comment}
          pending={action.isPending}
          error={action.error}
          onSubmit={(rating, comment) => action.mutate({ path: '/rate', body: { rating, comment } })}
        />
      </Card>
    )
  }

  if (!satisfaction) return null
  return (
    <Card>
      <h2 className="mb-2 text-lg font-semibold">{isRequester ? 'Tu calificación' : 'Calificación del solicitante'}</h2>
      <p className="text-2xl">
        <Stars rating={satisfaction.rating} />
      </p>
      {satisfaction.comment && <p className="mt-2 whitespace-pre-wrap text-slate-700">“{satisfaction.comment}”</p>}
      <p className="mt-2 text-sm text-slate-500">{formatDate(satisfaction.ratedAt)}</p>
    </Card>
  )
}

function Conversation({ ticket, isAgentView, finished }: { ticket: TicketDetail; isAgentView: boolean; finished: boolean }) {
  const action = useTicketAction(ticket.id)
  const [body, setBody] = useState('')
  const [visibility, setVisibility] = useState<MessageVisibility>('Public')
  const files = usePendingFiles()

  const submit = (event: FormEvent) => {
    event.preventDefault()
    action.mutate(
      { path: '/messages', body: { body, visibility, attachmentIds: files.ids } },
      { onSuccess: () => { setBody(''); files.clear() } },
    )
  }

  return (
    <Card>
      <h2 className="mb-4 text-lg font-semibold">Conversación</h2>
      {ticket.messages.length === 0 && <p className="mb-4 text-slate-500">Todavía no hay mensajes.</p>}
      <ul className="mb-6 space-y-3">
        {ticket.messages.map((m) => (
          <li
            key={m.id}
            className={`rounded-lg p-3 ${m.visibility === 'Public' ? 'bg-slate-100' : 'border border-dashed border-amber-400 bg-amber-50'}`}
          >
            <div className="mb-1 text-sm text-slate-500">
              <span className="font-medium text-slate-700">{m.author.displayName}</span> · {formatDate(m.createdAt)}
              {m.visibility !== 'Public' && <span className="ml-2 text-amber-800">Nota interna</span>}
            </div>
            <p className="whitespace-pre-wrap">{m.body}</p>
            <AttachmentList attachments={m.attachments} />
          </li>
        ))}
      </ul>
      {ticket.status !== 'Closed' && (
        <form onSubmit={submit} className="space-y-3">
          <textarea
            rows={3}
            value={body}
            onChange={(e) => setBody(e.target.value)}
            placeholder={finished && !isAgentView ? 'Si sigue pasando, escríbenos y la reabrimos.' : 'Escribe un mensaje…'}
            className="w-full rounded-lg border border-slate-300 px-3 py-2.5"
          />
          <FilePicker pending={files} />
          <div className="flex flex-wrap items-center gap-3">
            {isAgentView && (
              <select
                value={visibility}
                onChange={(e) => setVisibility(e.target.value as MessageVisibility)}
                className="rounded-lg border border-slate-300 px-3 py-2.5"
              >
                <option value="Public">Respuesta al solicitante</option>
                <option value="InternalAllAreas">Nota interna (todas las áreas)</option>
                <option value="InternalOwnArea">Nota interna (solo mi área)</option>
              </select>
            )}
            <Button type="submit" disabled={action.isPending || files.uploading || (!body.trim() && files.ids.length === 0)}>
              Enviar
            </Button>
          </div>
          <ErrorText error={action.error} />
        </form>
      )}
    </Card>
  )
}

/** The internal escalation chain, with the actions the agent's areas can take. */
function Chain({ ticket, me }: { ticket: TicketDetail; me: Me }) {
  const action = useTicketAction(ticket.id)
  const segments = ticket.segments ?? []
  const anyArea = me.roles.some((r) => r === 'Admin' || r === 'Supervisor')
  const isMine = (s: Segment) => anyArea || me.areas.some((a) => a.id === s.areaId)
  const hasOpenChild = (s: Segment) => segments.some((c) => c.parentSegmentId === s.id && c.status === 'Open')
  const finished = ticket.status === 'Resolved' || ticket.status === 'Closed'

  // Segments whose turn it is and that the agent can act on.
  const actionable = finished ? [] : segments.filter((s) => s.status === 'Open' && isMine(s) && !hasOpenChild(s))

  return (
    <Card>
      <h2 className="mb-4 text-lg font-semibold">Áreas que participan</h2>
      <ol className="mb-6 space-y-2">
        {segments.map((s) => (
          <li key={s.id} className="flex flex-wrap items-baseline gap-2">
            <span className={`h-2.5 w-2.5 rounded-full ${s.status === 'Open' ? 'bg-amber-500' : 'bg-emerald-500'}`} />
            <span className="font-medium">{s.areaName}</span>
            <span className="text-sm text-slate-500">
              {s.status === 'Open' ? (hasOpenChild(s) ? 'esperando a otra área' : 'trabajando') : 'resuelto'}
            </span>
            {!(s.status === 'Open' && hasOpenChild(s)) && <SlaBadge sla={s.sla} />}
            {s.escalationReason && (
              <span className="w-full pl-4 text-sm text-slate-600">
                {s.escalatedBy?.displayName} escaló: “{s.escalationReason}”
              </span>
            )}
            {s.resolutionNote && <span className="w-full pl-4 text-sm text-slate-600">Resolución: “{s.resolutionNote}”</span>}
          </li>
        ))}
      </ol>

      {!finished && segments.some(isMine) && <TicketControls ticket={ticket} />}

      {actionable.map((s) => (
        <SegmentActions key={s.id} ticket={ticket} segment={s} />
      ))}

      {!finished && actionable.length > 0 && (
        <div className="mt-4 border-t border-slate-200 pt-4">
          <Button onClick={() => action.mutate({ path: '/resolve', body: { note: null } })} disabled={action.isPending}>
            Resolver todo
          </Button>
          <p className="mt-2 text-sm text-slate-500">Cierra el ticket para toda la cadena y avisa al solicitante.</p>
          <ErrorText error={action.error} />
        </div>
      )}
    </Card>
  )
}

function SegmentActions({ ticket, segment }: { ticket: TicketDetail; segment: Segment }) {
  const action = useTicketAction(ticket.id)
  const { data: areas } = useAreas(true)
  const [toAreaId, setToAreaId] = useState<number | ''>('')
  const [reason, setReason] = useState('')
  const [note, setNote] = useState('')
  const isRoot = segment.parentSegmentId === null

  const escalate = (event: FormEvent) => {
    event.preventDefault()
    action.mutate(
      { path: '/escalate', body: { segmentId: segment.id, toAreaId, reason } },
      { onSuccess: () => { setReason(''); setToAreaId('') } },
    )
  }

  return (
    <div className="space-y-4 rounded-lg bg-slate-50 p-4">
      <p className="font-medium">Como {segment.areaName}:</p>
      <form onSubmit={escalate} className="flex flex-wrap gap-3">
        <select
          required
          value={toAreaId}
          onChange={(e) => setToAreaId(e.target.value ? Number(e.target.value) : '')}
          className="rounded-lg border border-slate-300 px-3 py-2.5"
        >
          <option value="">Escalar a…</option>
          {areas
            ?.filter((a) => a.id !== segment.areaId)
            .map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
        </select>
        <input
          required
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="¿Qué necesitas que revisen?"
          className="min-w-60 flex-1 rounded-lg border border-slate-300 px-3 py-2.5"
        />
        <Button type="submit" variant="secondary" disabled={action.isPending}>
          Escalar
        </Button>
      </form>
      {!isRoot && (
        <div className="flex flex-wrap gap-3">
          <input
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="¿Qué encontraste? (lo verá el área que escaló)"
            className="min-w-60 flex-1 rounded-lg border border-slate-300 px-3 py-2.5"
          />
          <Button
            variant="secondary"
            disabled={action.isPending}
            onClick={() => action.mutate({ path: `/segments/${segment.id}/resolve`, body: { note } })}
          >
            Resolver mi parte
          </Button>
        </div>
      )}
      <ErrorText error={action.error} />
    </div>
  )
}

/** Priority (which sets the SLA targets) and pausing the clock while the requester owes an answer. */
function TicketControls({ ticket }: { ticket: TicketDetail }) {
  const action = useTicketAction(ticket.id)
  const waiting = ticket.status === 'WaitingOnRequester'

  return (
    <div className="mb-4 flex flex-wrap items-center gap-3">
      <label className="flex items-center gap-2">
        <span className="font-medium">Prioridad</span>
        <select
          value={ticket.priority}
          disabled={action.isPending}
          onChange={(e) => action.mutate({ path: '/priority', body: { priority: e.target.value as TicketPriority } })}
          className="rounded-lg border border-slate-300 px-3 py-2.5"
        >
          {priorities.map((p) => (
            <option key={p} value={p}>
              {priorityLabel(p)}
            </option>
          ))}
        </select>
      </label>
      {waiting ? (
        <span className="text-sm text-slate-600">Esperando respuesta del solicitante: el SLA está en pausa.</span>
      ) : (
        <Button variant="secondary" disabled={action.isPending} onClick={() => action.mutate({ path: '/wait' })}>
          Esperar respuesta del solicitante
        </Button>
      )}
      <ErrorText error={action.error} />
    </div>
  )
}
