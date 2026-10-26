import { useState, type FormEvent } from 'react'
import { useAreas, useSaveArea } from '../api/hooks'
import type { Area } from '../api/types'
import { Button, Card, ErrorText } from '../components/ui'

/** Administrators define the areas; who belongs to each one is set on the Users page. */
export default function AreasPage() {
  const { data: areas, isLoading, error } = useAreas(true)
  const [editing, setEditing] = useState<Area | 'new' | null>(null)

  return (
    <section className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Áreas</h1>
        <Button onClick={() => setEditing('new')}>Nueva área</Button>
      </div>
      {editing && <AreaForm area={editing === 'new' ? null : editing} onDone={() => setEditing(null)} />}
      {isLoading && <p className="text-slate-500">Cargando…</p>}
      <ErrorText error={error} />
      <ul className="space-y-3">
        {areas?.map((area) => (
          <li key={area.id} className="flex flex-wrap items-center gap-3 rounded-xl border border-slate-200 bg-white p-4">
            <div className="flex-1">
              <p className="font-medium">{area.name}</p>
              {area.description && <p className="text-sm text-slate-600">{area.description}</p>}
              {!area.acceptsRequests && <p className="text-sm text-slate-500">Solo recibe tickets escalados</p>}
            </div>
            <Button variant="secondary" onClick={() => setEditing(area)}>
              Editar
            </Button>
          </li>
        ))}
      </ul>
    </section>
  )
}

function AreaForm({ area, onDone }: { area: Area | null; onDone: () => void }) {
  const save = useSaveArea()
  const [name, setName] = useState(area?.name ?? '')
  const [description, setDescription] = useState(area?.description ?? '')
  const [acceptsRequests, setAcceptsRequests] = useState(area?.acceptsRequests ?? true)

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate({ id: area?.id, name, description: description || null, acceptsRequests }, { onSuccess: onDone })
  }

  return (
    <Card>
      <form onSubmit={submit} className="space-y-4">
        <h2 className="text-lg font-semibold">{area ? `Editar ${area.name}` : 'Nueva área'}</h2>
        <label className="block">
          <span className="mb-1 block font-medium">Nombre</span>
          <input required value={name} onChange={(e) => setName(e.target.value)} className="w-full rounded-lg border border-slate-300 px-3 py-2.5" />
        </label>
        <label className="block">
          <span className="mb-1 block font-medium">Descripción para el solicitante</span>
          <input
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Ejemplo: Facturas, pagos y reembolsos"
            className="w-full rounded-lg border border-slate-300 px-3 py-2.5"
          />
        </label>
        <label className="flex items-center gap-2">
          <input type="checkbox" checked={acceptsRequests} onChange={(e) => setAcceptsRequests(e.target.checked)} />
          Aparece en "¿Con quién necesitas ayuda?" (si no, solo recibe tickets escalados)
        </label>
        <ErrorText error={save.error} />
        <div className="flex gap-3">
          <Button type="submit" disabled={save.isPending}>
            Guardar
          </Button>
          <Button type="button" variant="secondary" onClick={onDone}>
            Cancelar
          </Button>
        </div>
      </form>
    </Card>
  )
}
