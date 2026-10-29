import { useRef } from 'react'
import type { Attachment } from '../api/types'
import { formatSize } from '../format'
import { maxFiles, type PendingFiles } from './usePendingFiles'

const fileUrl = (a: Attachment) => `/api/attachments/${a.id}`

/** "Adjuntar archivos" button plus the picked files, each with its upload state and a way to drop it. */
export function FilePicker({ pending }: { pending: PendingFiles }) {
  const input = useRef<HTMLInputElement>(null)

  return (
    <div className="space-y-2">
      <input
        ref={input}
        type="file"
        multiple
        className="sr-only"
        tabIndex={-1}
        aria-hidden="true"
        onChange={(e) => {
          if (e.target.files) pending.add(e.target.files)
          e.target.value = ''
        }}
      />
      <button
        type="button"
        disabled={pending.full}
        onClick={() => input.current?.click()}
        className="inline-flex items-center gap-2 rounded-lg border border-dashed border-slate-400 px-3 py-2 text-slate-700 hover:border-brand-600 hover:text-brand-700 disabled:cursor-not-allowed disabled:text-slate-400"
      >
        <span aria-hidden="true">📎</span>
        {pending.full ? `Máximo ${maxFiles} archivos` : 'Adjuntar archivos'}
      </button>
      {pending.files.length > 0 && (
        <ul className="flex flex-wrap gap-2" aria-label="Archivos adjuntos">
          {pending.files.map((f) => (
            <li
              key={f.key}
              className={`flex max-w-full items-center gap-2 rounded-lg border px-3 py-1.5 text-sm ${f.error ? 'border-red-300 bg-red-50 text-red-800' : 'border-slate-200 bg-slate-50'}`}
            >
              <span className="truncate">{f.name}</span>
              <span className="shrink-0 text-slate-500">
                {f.error ? f.error : f.attachment ? formatSize(f.size) : 'Subiendo…'}
              </span>
              <button
                type="button"
                onClick={() => pending.remove(f.key)}
                className="shrink-0 rounded px-1 text-slate-500 hover:text-slate-900"
                aria-label={`Quitar ${f.name}`}
              >
                ✕
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/** Images as thumbnails that open full size; other files as downloads with their size. */
export function AttachmentList({ attachments }: { attachments: Attachment[] }) {
  if (attachments.length === 0) return null
  const images = attachments.filter((a) => a.isImage)
  const files = attachments.filter((a) => !a.isImage)

  return (
    <div className="mt-3 space-y-2">
      {images.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {images.map((a) => (
            <a key={a.id} href={fileUrl(a)} target="_blank" rel="noreferrer" title={a.fileName}>
              <img
                src={fileUrl(a)}
                alt={a.fileName}
                loading="lazy"
                className="h-24 w-32 rounded-lg border border-slate-200 bg-white object-cover"
              />
            </a>
          ))}
        </div>
      )}
      {files.length > 0 && (
        <ul className="flex flex-wrap gap-2">
          {files.map((a) => (
            <li key={a.id}>
              <a
                href={fileUrl(a)}
                download={a.fileName}
                className="inline-flex max-w-full items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-brand-700 hover:bg-slate-50"
              >
                <span aria-hidden="true">📄</span>
                <span className="truncate underline">{a.fileName}</span>
                <span className="shrink-0 text-slate-500">{formatSize(a.size)}</span>
              </a>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
