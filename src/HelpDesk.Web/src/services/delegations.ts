export interface DelegationItem {
  id: number
  sourceUserId: number
  sourceName: string
  targetUserId: number
  targetName: string
  startsAtUtc: string
  endsAtUtc: string
  reason: string | null
  status: string
  issue: string | null
  totalTickets: number
  pendingTickets: number
  returnedTickets: number
  skippedClosedTickets: number
  skippedReassignedTickets: number
  stoppedAtUtc: string | null
  stoppedByName: string | null
  canCancel: boolean
  canEndEarly: boolean
  rowVersion: string
}
export interface DelegationPage {
  items: DelegationItem[]
  total: number
  page: number
}
export interface DelegationDetail {
  delegation: DelegationItem
  total: number
  page: number
  tickets: {
    ticketId: number
    ticketNumber: string
    title: string
    status: string
    currentAssigneeName: string | null
    entryReason: string
    returnOutcome: string
    delegatedAtUtc: string
    processedAtUtc: string | null
  }[]
}
export const delegationStatuses: Record<string, string> = {
  Scheduled: 'Scheduled',
  Active: 'Active',
  AwaitingActivation: 'Waiting to start',
  Returning: 'Returning tickets',
  NeedsAttention: 'Needs attention',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
  EndedEarly: 'Ended early',
}
export const returnOutcomes: Record<string, string> = {
  AwaitingEnd: 'Awaiting return',
  Returned: 'Returned',
  SkippedClosed: 'Kept — ticket closed',
  SkippedReassigned: 'Kept — reassigned by admin',
}
export const malaysiaDate = (value: string) =>
  new Intl.DateTimeFormat('en-MY', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Asia/Kuala_Lumpur',
  }).format(new Date(value))
export const malaysiaInput = (value: Date) =>
  new Date(value.getTime() + 8 * 3600000).toISOString().slice(0, 10)
export function delegationPeriod(item: DelegationItem) {
  const end = new Date(item.endsAtUtc)
  // Preserve exact times for arrangements created before date-only scheduling.
  if ((end.getTime() + 8 * 3600000) % 86400000 !== 0)
    return `${malaysiaDate(item.startsAtUtc)} → ${malaysiaDate(item.endsAtUtc)} (MYT)`
  const date = new Intl.DateTimeFormat('en-MY', {
    dateStyle: 'medium',
    timeZone: 'Asia/Kuala_Lumpur',
  })
  return `${date.format(new Date(item.startsAtUtc))} → ${date.format(new Date(end.getTime() - 1))} (MYT, inclusive)`
}
