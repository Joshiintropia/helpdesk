// Mirrors HelpDesk.Api.Endpoints.Contracts.

export type TicketStatus = 'New' | 'InProgress' | 'WaitingOnRequester' | 'Resolved' | 'Closed'
export type TicketPriority = 'Low' | 'Normal' | 'High' | 'Urgent'
export type SegmentStatus = 'Open' | 'Resolved'
export type MessageVisibility = 'Public' | 'InternalAllAreas' | 'InternalOwnArea'

export interface Area {
  id: number
  name: string
  description: string | null
  acceptsRequests: boolean
}

export interface Me {
  id: string
  email: string
  displayName: string
  roles: string[]
  areas: Area[]
}

export interface Person {
  id: string
  displayName: string
}

export interface TicketSummary {
  id: number
  title: string
  status: TicketStatus
  priority: TicketPriority
  currentAreaId: number
  currentAreaName: string
  createdAt: string
  updatedAt: string
}

export interface Segment {
  id: string
  areaId: number
  areaName: string
  parentSegmentId: string | null
  status: SegmentStatus
  escalationReason: string | null
  escalatedBy: Person | null
  assignee: Person | null
  openedAt: string
  resolvedAt: string | null
  resolutionNote: string | null
}

export interface Message {
  id: string
  author: Person
  authorAreaId: number | null
  body: string
  visibility: MessageVisibility
  source: 'Portal' | 'Email' | 'System'
  createdAt: string
}

export interface TicketDetail {
  id: number
  title: string
  description: string
  status: TicketStatus
  priority: TicketPriority
  requester: Person
  currentAreaId: number
  currentAreaName: string
  createdAt: string
  updatedAt: string
  resolvedAt: string | null
  /** Only present for agents: the escalation chain is internal. */
  segments: Segment[] | null
  messages: Message[]
}

export interface QueueItem {
  ticketId: number
  title: string
  status: TicketStatus
  priority: TicketPriority
  segmentId: string
  areaId: number
  assigneeId: string | null
  escalationReason: string | null
  openedAt: string
  ticketCreatedAt: string
}
