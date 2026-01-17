/**
 * Touch Controller Module
 *
 * Provides virtual keys for mobile terminal interaction.
 * Includes arrow keys, modifiers (Ctrl/Alt/Shift), and special keys.
 */

export {
  initTouchController,
  destroyTouchController,
  showTouchController,
  hideTouchController,
  updateVisibility,
} from './bar';

export type { ModifierKey } from './modifiers';
