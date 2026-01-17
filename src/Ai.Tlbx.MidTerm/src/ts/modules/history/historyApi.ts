/**
 * History API Module
 *
 * API client for backend-persisted launch history.
 */

export interface LaunchEntry {
  id: string;
  shellType: string;
  executable: string;
  commandLine: string | null;
  workingDirectory: string;
  isStarred: boolean;
  weight: number;
  lastUsed: string;
}

export async function fetchHistory(): Promise<LaunchEntry[]> {
  const res = await fetch('/api/history');
  if (!res.ok) {
    throw new Error(`Failed to fetch history: ${res.status}`);
  }
  return res.json();
}

export async function toggleStar(id: string): Promise<boolean> {
  const res = await fetch(`/api/history/${id}/star`, { method: 'PUT' });
  return res.ok;
}

export async function removeHistoryEntry(id: string): Promise<boolean> {
  const res = await fetch(`/api/history/${id}`, { method: 'DELETE' });
  return res.ok;
}
