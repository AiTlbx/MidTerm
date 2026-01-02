/**
 * Auth Status Module
 *
 * Handles authentication status checking and security warning display.
 */

import { authStatus, setAuthStatus } from '../../state';
import type { AuthStatus } from '../../types';

/**
 * Check authentication status from server
 */
export async function checkAuthStatus(): Promise<void> {
  try {
    const response = await fetch('/api/auth/status');

    if (response.status === 401) {
      window.location.href = '/login.html';
      return;
    }

    const status: AuthStatus = await response.json();
    setAuthStatus(status);
    updateSecurityWarning();
    updatePasswordStatus();
  } catch (e) {
    console.error('Auth status error:', e);
  }
}

/**
 * Update security warning visibility based on auth status
 */
export function updateSecurityWarning(): void {
  const warning = document.getElementById('security-warning');
  if (!warning) return;

  if (authStatus && authStatus.authenticationEnabled && !authStatus.passwordSet) {
    warning.classList.remove('hidden');
  } else {
    warning.classList.add('hidden');
  }
}

/**
 * Update password status text in settings panel
 */
export function updatePasswordStatus(): void {
  const statusEl = document.getElementById('password-status-text');
  if (!statusEl) return;

  if (!authStatus) {
    statusEl.textContent = 'Checking...';
    statusEl.className = '';
    return;
  }

  if (authStatus.passwordSet) {
    statusEl.textContent = 'Password is set';
    statusEl.className = 'status-set';
  } else {
    statusEl.textContent = 'No password set';
    statusEl.className = 'status-missing';
  }
}

/**
 * Dismiss the security warning banner
 */
export function dismissSecurityWarning(): void {
  const warning = document.getElementById('security-warning');
  if (warning) {
    warning.classList.add('hidden');
  }
}
