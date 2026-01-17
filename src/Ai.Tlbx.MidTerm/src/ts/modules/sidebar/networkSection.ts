/**
 * Network Section Module
 *
 * Handles the collapsible "Network & Remote Access" section
 * in the sidebar footer.
 */

import { createLogger } from '../logging';

const log = createLogger('networkSection');
const STORAGE_KEY = 'midterm.networkSectionCollapsed';

export function initNetworkSection(): void {
  const section = document.getElementById('network-section');
  const toggleBtn = document.getElementById('btn-toggle-network');

  if (!section || !toggleBtn) {
    log.info(() => 'Network section elements not found');
    return;
  }

  const isCollapsed = localStorage.getItem(STORAGE_KEY) === 'true';
  if (isCollapsed) {
    section.classList.add('collapsed');
  }

  toggleBtn.addEventListener('click', () => {
    const nowCollapsed = section.classList.toggle('collapsed');
    localStorage.setItem(STORAGE_KEY, String(nowCollapsed));
    log.info(() => `Network section ${nowCollapsed ? 'collapsed' : 'expanded'}`);
  });

  log.info(() => `Network section initialized (collapsed=${isCollapsed})`);
}
