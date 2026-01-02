/**
 * Sidebar Collapse Module
 *
 * Handles sidebar visibility, collapse/expand state,
 * and island title updates for desktop view.
 */

import {
  sidebarOpen,
  sidebarCollapsed,
  setSidebarOpen,
  setSidebarCollapsed,
  sessions,
  activeSessionId,
  dom
} from '../../state';
import { getCookie, setCookie } from '../../utils';
import { getSessionDisplayName } from './sessionList';

// =============================================================================
// Cookie Constants
// =============================================================================

const SIDEBAR_COLLAPSED_COOKIE = 'mm-sidebar-collapsed';
const DESKTOP_BREAKPOINT = 768;

// =============================================================================
// Mobile Sidebar Toggle
// =============================================================================

/**
 * Toggle mobile sidebar visibility
 */
export function toggleSidebar(): void {
  setSidebarOpen(!sidebarOpen);
  if (dom.app) dom.app.classList.toggle('sidebar-open', sidebarOpen);
}

/**
 * Close mobile sidebar
 */
export function closeSidebar(): void {
  setSidebarOpen(false);
  if (dom.app) dom.app.classList.remove('sidebar-open');
}

// =============================================================================
// Desktop Sidebar Collapse
// =============================================================================

/**
 * Collapse sidebar to icon-only mode (desktop)
 */
export function collapseSidebar(): void {
  setSidebarCollapsed(true);
  if (dom.app) dom.app.classList.add('sidebar-collapsed');
  setCookie(SIDEBAR_COLLAPSED_COOKIE, 'true');
  updateIslandTitle();
}

/**
 * Expand sidebar to full width (desktop)
 */
export function expandSidebar(): void {
  setSidebarCollapsed(false);
  if (dom.app) dom.app.classList.remove('sidebar-collapsed');
  setCookie(SIDEBAR_COLLAPSED_COOKIE, 'false');
}

// =============================================================================
// Island Title
// =============================================================================

/**
 * Update the desktop island title with current session name
 */
export function updateIslandTitle(): void {
  if (!dom.islandTitle) return;
  const session = sessions.find((s) => s.id === activeSessionId);
  dom.islandTitle.textContent = session ? getSessionDisplayName(session) : 'MiddleManager';
}

// =============================================================================
// State Restoration
// =============================================================================

/**
 * Restore sidebar collapsed state from cookie (desktop only)
 */
export function restoreSidebarState(): void {
  if (getCookie(SIDEBAR_COLLAPSED_COOKIE) === 'true' && window.innerWidth > DESKTOP_BREAKPOINT) {
    setSidebarCollapsed(true);
    if (dom.app) dom.app.classList.add('sidebar-collapsed');
  }
}
