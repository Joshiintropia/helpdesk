import { useState } from 'react'
import { api } from '../api/client'
import type { Attachment } from '../api/types'

export const maxFiles = 5

export interface PendingFile {
  key: string
  name: string
  size: number
  attachment?: Attachment
  error?: string
}

/** Files picked for a request or message: each uploads as soon as it is picked, and is sent by id. */
export function usePendingFiles() {
  const [files, setFiles] = useState<PendingFile[]>([])

  const add = (picked: FileList | File[]) => {
    const room = maxFiles - files.length
    const accepted = Array.from(picked).slice(0, Math.max(0, room))
    const added = accepted.map((file) => ({ key: crypto.randomUUID(), name: file.name, size: file.size }))
    setFiles((current) => [...current, ...added])
    accepted.forEach((file, i) => {
      const key = added[i].key
      api
        .upload<Attachment>('/api/attachments', file)
        .then((attachment) => setFiles((current) => current.map((f) => (f.key === key ? { ...f, attachment } : f))))
        .catch((error: Error) => setFiles((current) => current.map((f) => (f.key === key ? { ...f, error: error.message } : f))))
    })
  }

  return {
    files,
    add,
    remove: (key: string) => setFiles((current) => current.filter((f) => f.key !== key)),
    clear: () => setFiles([]),
    ids: files.flatMap((f) => (f.attachment ? [f.attachment.id] : [])),
    uploading: files.some((f) => !f.attachment && !f.error),
    full: files.length >= maxFiles,
  }
}

export type PendingFiles = ReturnType<typeof usePendingFiles>
