import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { useAreas, useCreateTicket } from '../api/hooks'
import type { Area } from '../api/types'
import { Button, Card, ErrorText } from '../components/ui'

/** Two steps, no jargon: who can help you, and what is happening. */
export default function NewRequestPage() {
  const { data: areas, isLoading, error } = useAreas()
  const create = useCreateTicket()
  const navigate = useNavigate()
  const [area, setArea] = useState<Area | null>(null)
  const [description, setDescription] = useState('')

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (!area) return
    create.mutate(
      { areaId: area.id, description },
      { onSuccess: (ticket) => navigate(`/solicitudes/${ticket.id}`) },
    )
  }

  if (!area) {
    return (
      <section>
        <h1 className="mb-6 text-2xl font-semibold">¿Con quién necesitas ayuda?</h1>
        {isLoading && <p className="text-slate-500">Cargando…</p>}
        <ErrorText error={error} />
        {areas?.length === 0 && <p className="text-slate-600">Todavía no hay áreas configuradas.</p>}
        <div className="grid gap-4 sm:grid-cols-2">
          {areas?.map((a) => (
            <button
              key={a.id}
              onClick={() => setArea(a)}
              className="rounded-xl border-2 border-slate-200 bg-white p-6 text-left transition-colors hover:border-brand-600"
            >
              <span className="block text-lg font-semibold">{a.name}</span>
              {a.description && <span className="mt-1 block text-slate-600">{a.description}</span>}
            </button>
          ))}
        </div>
      </section>
    )
  }

  return (
    <Card>
      <button onClick={() => setArea(null)} className="mb-4 text-sm text-brand-700 underline">
        ← Cambiar área ({area.name})
      </button>
      <form onSubmit={submit} className="space-y-4">
        <label className="block">
          <span className="mb-1 block text-xl font-semibold">¿Qué está pasando?</span>
          <span className="mb-3 block text-slate-600">
            Cuéntalo con tus palabras: qué intentabas hacer, qué pasó y desde cuándo.
          </span>
          <textarea
            required
            rows={6}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Ejemplo: Ayer hice un pago y todavía no se ve reflejado en mi cuenta."
            className="w-full rounded-lg border border-slate-300 px-3 py-2.5"
          />
        </label>
        <ErrorText error={create.error} />
        <Button type="submit" disabled={create.isPending || !description.trim()}>
          {create.isPending ? 'Enviando…' : 'Enviar solicitud'}
        </Button>
      </form>
    </Card>
  )
}
