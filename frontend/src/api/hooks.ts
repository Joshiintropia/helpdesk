import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from './client'
import type { Area, Me, QueueItem, TicketDetail, TicketSummary } from './types'

export function useMe() {
  return useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      try {
        return await api.get<Me>('/api/me')
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null
        throw error
      }
    },
    staleTime: 60_000,
  })
}

export function useLogin() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: { email: string; password: string }) => api.post('/api/auth/login?useCookies=true', input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['me'] }),
  })
}

export function useLogout() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => api.post('/api/auth/logout'),
    onSuccess: () => client.clear(),
  })
}

export function useAreas(all = false) {
  return useQuery({
    queryKey: ['areas', all],
    queryFn: () => api.get<Area[]>(`/api/areas${all ? '?all=true' : ''}`),
  })
}

export function useMyTickets() {
  return useQuery({ queryKey: ['tickets', 'mine'], queryFn: () => api.get<TicketSummary[]>('/api/tickets/mine') })
}

export function useQueue() {
  return useQuery({
    queryKey: ['tickets', 'queue'],
    queryFn: () => api.get<QueueItem[]>('/api/tickets/queue'),
    refetchInterval: 30_000,
  })
}

export function useTicket(id: number) {
  return useQuery({ queryKey: ['tickets', id], queryFn: () => api.get<TicketDetail>(`/api/tickets/${id}`) })
}

export function useCreateTicket() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: { areaId: number; description: string }) => api.post<TicketDetail>('/api/tickets', input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['tickets'] }),
  })
}

/** Any action on one ticket: the API answers with the updated ticket, which replaces the cached one. */
export function useTicketAction(id: number) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ path, body }: { path: string; body?: unknown }) => api.post<TicketDetail>(`/api/tickets/${id}${path}`, body),
    onSuccess: (ticket) => {
      client.setQueryData(['tickets', id], ticket)
      client.invalidateQueries({ queryKey: ['tickets', 'mine'] })
      client.invalidateQueries({ queryKey: ['tickets', 'queue'] })
    },
  })
}
