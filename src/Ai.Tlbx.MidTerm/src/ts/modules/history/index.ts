/**
 * History Module
 *
 * Exports command history functionality.
 * Backend-persisted via /api/history endpoints.
 */

export {
  initHistoryDropdown,
  toggleHistoryDropdown,
  openHistoryDropdown,
  closeHistoryDropdown,
  refreshHistory,
  type LaunchEntry,
} from './historyDropdown';

export { fetchHistory, toggleStar, removeHistoryEntry } from './historyApi';

// Deprecated: No-op for backwards compatibility
export function initializeCommandHistory(): void {
  // Backend handles history now - no localStorage initialization needed
}
