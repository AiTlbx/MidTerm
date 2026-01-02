/**
 * Settings Panel Module
 *
 * Handles the settings panel UI visibility, system status display,
 * and health check functionality.
 */

import type { HealthResponse } from '../../types';
import {
  settingsOpen,
  setSettingsOpen,
  activeSessionId,
  sessionTerminals,
  sessions,
  dom
} from '../../state';
import { fetchSettings } from './persistence';

/**
 * Close the mobile sidebar
 */
function closeSidebar(): void {
  const app = dom.app;
  if (app) app.classList.remove('sidebar-open');
}

/**
 * Toggle the settings panel visibility
 */
export function toggleSettings(): void {
  if (settingsOpen) {
    closeSettings();
  } else {
    openSettings();
  }
}

/**
 * Open the settings panel
 */
export function openSettings(): void {
  setSettingsOpen(true);
  if (dom.settingsBtn) dom.settingsBtn.classList.add('active');
  closeSidebar();

  if (activeSessionId) {
    const state = sessionTerminals.get(activeSessionId);
    if (state) state.container.classList.add('hidden');
  }

  if (dom.emptyState) dom.emptyState.classList.add('hidden');
  if (dom.settingsView) dom.settingsView.classList.remove('hidden');

  fetchSettings();
  fetchSystemStatus();
}

/**
 * Close the settings panel
 */
export function closeSettings(): void {
  setSettingsOpen(false);
  if (dom.settingsBtn) dom.settingsBtn.classList.remove('active');
  if (dom.settingsView) dom.settingsView.classList.add('hidden');

  if (activeSessionId) {
    const state = sessionTerminals.get(activeSessionId);
    if (state) {
      state.container.classList.remove('hidden');
      requestAnimationFrame(() => {
        state.terminal.focus();
      });
    }
  } else if (sessions.length === 0 && dom.emptyState) {
    dom.emptyState.classList.remove('hidden');
  }
}

/**
 * Format uptime seconds into human-readable string
 */
export function formatUptime(seconds: number): string {
  if (seconds < 60) return seconds + 's';
  if (seconds < 3600) return Math.floor(seconds / 60) + 'm ' + (seconds % 60) + 's';

  const hours = Math.floor(seconds / 3600);
  const mins = Math.floor((seconds % 3600) / 60);

  if (hours < 24) return hours + 'h ' + mins + 'm';

  const days = Math.floor(hours / 24);
  return days + 'd ' + (hours % 24) + 'h';
}

/**
 * Update the con-host version mismatch warning banner
 */
export function updateConHostWarning(health: HealthResponse): void {
  let banner = document.getElementById('conhost-warning');

  if (!banner) {
    banner = document.createElement('div');
    banner.id = 'conhost-warning';
    banner.className = 'warning-banner';
    const header = document.querySelector('.app-header');
    if (header && header.parentNode) {
      header.parentNode.insertBefore(banner, header.nextSibling);
    }
  }

  const healthAny = health as HealthResponse & {
    conHostCompatible?: boolean;
    conHostExpected?: string;
  };

  if (health.conHostVersion && healthAny.conHostCompatible === false) {
    banner.innerHTML =
      '<strong>Version mismatch:</strong> mm-con-host is ' +
      health.conHostVersion +
      ', expected ' +
      healthAny.conHostExpected +
      '. Terminals may not work correctly. Please update mm-con-host.exe or restart the service.';
    banner.style.display = 'block';
  } else {
    banner.style.display = 'none';
  }
}

/**
 * Fetch and display system status in the settings panel
 */
export function fetchSystemStatus(): void {
  const container = document.getElementById('system-status-content');
  if (!container) return;

  fetch('/api/health')
    .then((response) => response.json() as Promise<HealthResponse & {
      healthy?: boolean;
      uptimeSeconds?: number;
      mode?: string;
      platform?: string;
      webProcessId?: number;
      conHostCompatible?: boolean;
      conHostExpected?: string;
    }>)
    .then((health) => {
      const statusClass = health.healthy ? 'status-healthy' : 'status-error';
      const statusText = health.healthy ? 'Healthy' : 'Unhealthy';
      const uptimeStr = formatUptime(health.uptimeSeconds || 0);

      let conHostHtml = '';
      if (health.conHostVersion !== null && health.conHostVersion !== undefined) {
        const versionClass = health.conHostCompatible ? '' : 'status-error';
        conHostHtml =
          '<div class="status-detail-row">' +
          '<span class="detail-label">mm-con-host</span>' +
          '<span class="detail-value ' +
          versionClass +
          '">' +
          health.conHostVersion +
          (health.conHostCompatible ? '' : ' expected ' + health.conHostExpected) +
          '</span>' +
          '</div>';
      }

      container.innerHTML =
        '<div class="status-grid">' +
        '<div class="status-item">' +
        '<span class="status-label">Status</span>' +
        '<span class="status-value ' +
        statusClass +
        '">' +
        statusText +
        '</span>' +
        '</div>' +
        '<div class="status-item">' +
        '<span class="status-label">Mode</span>' +
        '<span class="status-value">' +
        (health.mode || '') +
        '</span>' +
        '</div>' +
        '<div class="status-item">' +
        '<span class="status-label">Sessions</span>' +
        '<span class="status-value">' +
        health.sessionCount +
        '</span>' +
        '</div>' +
        '<div class="status-item">' +
        '<span class="status-label">Uptime</span>' +
        '<span class="status-value">' +
        uptimeStr +
        '</span>' +
        '</div>' +
        '</div>' +
        '<div class="status-details">' +
        '<div class="status-detail-row">' +
        '<span class="detail-label">Platform</span>' +
        '<span class="detail-value">' +
        (health.platform || '') +
        '</span>' +
        '</div>' +
        '<div class="status-detail-row">' +
        '<span class="detail-label">Process ID</span>' +
        '<span class="detail-value">' +
        (health.webProcessId || '') +
        '</span>' +
        '</div>' +
        conHostHtml +
        '</div>';

      updateConHostWarning(health);
    })
    .catch((err: Error) => {
      container.innerHTML =
        '<div class="status-error-msg">Failed to load system status: ' + err.message + '</div>';
    });
}

/**
 * Check system health on startup for version mismatches
 */
export function checkSystemHealth(): void {
  fetch('/api/health')
    .then((response) => response.json() as Promise<HealthResponse>)
    .then((health) => {
      updateConHostWarning(health);
    })
    .catch(() => {});
}
