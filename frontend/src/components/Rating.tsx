import { useState, type FormEvent } from 'react'
import { Button, ErrorText } from './ui'

const labels = ['Muy mala', 'Mala', 'Regular', 'Buena', 'Excelente']

/** One to five stars plus an optional comment. The stars are a radio group, so the keyboard works too. */
export function RatingForm({
  initialRating,
  initialComment,
  onSubmit,
  pending,
  error,
}: {
  initialRating?: number | null
  initialComment?: string | null
  onSubmit: (rating: number, comment: string | null) => void
  pending: boolean
  error: unknown
}) {
  const [rating, setRating] = useState(initialRating ?? 0)
  const [hover, setHover] = useState(0)
  const [comment, setComment] = useState(initialComment ?? '')
  const shown = hover || rating

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (rating) onSubmit(rating, comment.trim() || null)
  }

  return (
    <form onSubmit={submit} className="space-y-3">
      <fieldset>
        <legend className="mb-2 font-medium">¿Cómo calificas la atención?</legend>
        <div className="flex items-center gap-1" onMouseLeave={() => setHover(0)}>
          {labels.map((label, i) => {
            const value = i + 1
            return (
              <label key={value} className="cursor-pointer" onMouseEnter={() => setHover(value)}>
                <input
                  type="radio"
                  name="rating"
                  value={value}
                  checked={rating === value}
                  onChange={() => setRating(value)}
                  className="peer sr-only"
                />
                <span
                  aria-hidden="true"
                  className={`block rounded px-0.5 text-4xl leading-none peer-focus-visible:outline-2 peer-focus-visible:outline-brand-600 ${value <= shown ? 'text-amber-400' : 'text-slate-300'}`}
                >
                  ★
                </span>
                <span className="sr-only">
                  {value} de 5: {label}
                </span>
              </label>
            )
          })}
          <span className="ml-3 text-slate-600">{shown ? labels[shown - 1] : ''}</span>
        </div>
      </fieldset>
      <label className="block">
        <span className="mb-1 block text-slate-700">¿Algo que quieras contarnos? (opcional)</span>
        <textarea
          rows={2}
          maxLength={2000}
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          className="w-full rounded-lg border border-slate-300 px-3 py-2.5"
        />
      </label>
      <ErrorText error={error} />
      <Button type="submit" disabled={pending || !rating}>
        {pending ? 'Enviando…' : 'Enviar calificación'}
      </Button>
    </form>
  )
}

/** A rating already given, read-only. */
export function Stars({ rating }: { rating: number }) {
  return (
    <span className="text-amber-400" aria-label={`${rating} de 5: ${labels[rating - 1]}`}>
      {'★'.repeat(rating)}
      <span className="text-slate-300">{'★'.repeat(5 - rating)}</span>
    </span>
  )
}
