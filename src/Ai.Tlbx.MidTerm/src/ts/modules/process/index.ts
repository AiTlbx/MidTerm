/**
 * Process Module
 *
 * Exports process monitoring functionality.
 */

export {
  registerProcessStateCallback,
  getProcessState,
  handleProcessEvent,
  handleForegroundChange,
  clearProcessState,
  getRacingLogText,
  getFullRacingLog,
  isRacingLogVisible,
  getForegroundInfo,
} from './processMonitor';
