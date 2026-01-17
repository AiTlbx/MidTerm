/**
 * Touch Controller Constants
 *
 * Escape sequences and key mappings for terminal input.
 */

/** ANSI escape sequences for special keys */
export const KEY_SEQUENCES: Record<string, string> = {
  // Arrow keys
  up: '\x1b[A',
  down: '\x1b[B',
  right: '\x1b[C',
  left: '\x1b[D',

  // Navigation
  home: '\x1b[H',
  end: '\x1b[F',
  pgup: '\x1b[5~',
  pgdn: '\x1b[6~',
  insert: '\x1b[2~',
  delete: '\x1b[3~',

  // Control characters
  tab: '\t',
  enter: '\r',
  esc: '\x1b',
  backspace: '\x7f',

  // Ctrl combinations (sent directly)
  ctrlc: '\x03',
  ctrld: '\x04',
  ctrlz: '\x1a',
  ctrla: '\x01',
  ctrle: '\x05',
};

/** CSS class names */
export const CSS_CLASSES = {
  controller: 'touch-controller',
  visible: 'visible',
  expanded: 'expanded',
  active: 'active',
  touchMode: 'touch-mode',
  panelExpanded: 'touch-panel-expanded',
} as const;

/** DOM Selectors */
export const SELECTORS = {
  controller: '#touch-controller',
  panel: '#touch-panel',
  expandButton: '.touch-expand',
  modifierKey: '.touch-modifier',
  actionKey: '.touch-key:not(.touch-modifier):not(.touch-expand)',
} as const;
