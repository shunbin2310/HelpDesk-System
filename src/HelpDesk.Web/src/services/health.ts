export interface HealthResponse {
  status: string
  service: string
  timestamp: string
}

export async function getHealth(): Promise<HealthResponse> {
  const response = await fetch('/api/health', {
    signal: AbortSignal.timeout(8000),
  })

  if (!response.ok) {
    throw new Error(`Health request failed (${response.status}).`)
  }

  return response.json() as Promise<HealthResponse>
}
