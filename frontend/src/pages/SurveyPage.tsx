import { useParams } from 'react-router'
import { useAnswerSurvey, useSurvey } from '../api/hooks'
import { RatingForm, Stars } from '../components/Rating'
import { Card } from '../components/ui'

/** Reached from the resolution email, without signing in: the link itself is the access. */
export default function SurveyPage() {
  const token = useParams().token ?? ''
  const { data: survey, isLoading, error } = useSurvey(token)
  const answer = useAnswerSurvey(token)

  return (
    <div className="min-h-screen">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto max-w-xl px-4 py-3 text-lg font-semibold">Mesa de ayuda</div>
      </header>
      <main className="mx-auto max-w-xl px-4 py-8">
        {isLoading && <p className="text-slate-500">Cargando…</p>}
        {error && (
          <Card>
            <h1 className="mb-2 text-xl font-semibold">Este enlace ya no es válido</h1>
            <p className="text-slate-600">Puede que haya vencido. Si quieres contarnos algo, responde el correo de tu solicitud.</p>
          </Card>
        )}
        {survey && (
          <Card>
            <p className="text-slate-500">Solicitud #{survey.ticketId}</p>
            <h1 className="mb-6 text-xl font-semibold">{survey.title}</h1>
            {answer.isSuccess && <p className="mb-4 rounded-lg bg-emerald-50 p-3 text-emerald-900">¡Gracias! Tu opinión nos ayuda a mejorar.</p>}
            {survey.canRate ? (
              <RatingForm
                initialRating={survey.rating}
                initialComment={survey.comment}
                pending={answer.isPending}
                error={answer.error}
                onSubmit={(rating, comment) => answer.mutate({ rating, comment })}
              />
            ) : survey.rating ? (
              <div>
                <p className="mb-1 font-medium">Tu calificación</p>
                <p className="text-3xl">
                  <Stars rating={survey.rating} />
                </p>
                {survey.comment && <p className="mt-2 text-slate-700">“{survey.comment}”</p>}
              </div>
            ) : (
              <p className="text-slate-600">Esta solicitud todavía no está resuelta. Podrás calificarla cuando la resolvamos.</p>
            )}
          </Card>
        )}
      </main>
    </div>
  )
}
