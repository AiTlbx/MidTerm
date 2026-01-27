/**
 * Auth Module
 *
 * Re-exports all authentication-related functionality.
 */

export {
  checkAuthStatus,
  updateSecurityWarning,
  updatePasswordStatus,
  dismissSecurityWarning,
  logout,
} from './status';

export {
  showPasswordModal,
  hidePasswordModal,
  handlePasswordSubmit,
  showPasswordError,
  bindAuthEvents,
} from './password';
