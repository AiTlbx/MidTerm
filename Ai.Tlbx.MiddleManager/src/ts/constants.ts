/**
 * Constants
 *
 * Protocol constants, theme definitions, and configuration values.
 */

import type { TerminalTheme, ThemeName } from './types';

// =============================================================================
// Mux Protocol Constants
// =============================================================================

/** Mux protocol header size (1 byte type + 8 byte session ID) */
export const MUX_HEADER_SIZE = 9;

/** Mux protocol message types */
export const MUX_TYPE_OUTPUT = 0x01;  // Server -> Client: Terminal output (includes dimensions)
export const MUX_TYPE_INPUT = 0x02;   // Client -> Server: Terminal input
export const MUX_TYPE_RESIZE = 0x03;  // Client -> Server: Terminal resize
export const MUX_TYPE_RESYNC = 0x05;  // Server -> Client: Clear terminals, buffer refresh follows

// =============================================================================
// Terminal Themes
// =============================================================================

/** Terminal color themes */
export const THEMES: Record<ThemeName, TerminalTheme> = {
  dark: {
    background: '#101014',
    foreground: '#DCDCF5',
    cursor: '#DCDCF5',
    cursorAccent: '#101014',
    selectionBackground: '#283457'
  },
  light: {
    background: '#D5D6DB',
    foreground: '#343B58',
    cursor: '#343B58',
    cursorAccent: '#D5D6DB',
    selectionBackground: '#9FA0A5'
  },
  solarizedDark: {
    background: '#002B36',
    foreground: '#839496',
    cursor: '#839496',
    cursorAccent: '#002B36',
    selectionBackground: '#073642'
  },
  solarizedLight: {
    background: '#FDF6E3',
    foreground: '#657B83',
    cursor: '#657B83',
    cursorAccent: '#FDF6E3',
    selectionBackground: '#EEE8D5'
  }
};

// =============================================================================
// Default Settings
// =============================================================================

/** Default terminal settings */
export const DEFAULT_SETTINGS = {
  fontSize: 14,
  scrollbackLines: 10000,
  cursorStyle: 'bar' as const,
  cursorBlink: true,
  theme: 'dark' as ThemeName,
  bellStyle: 'notification' as const,
  copyOnSelect: false,
  rightClickPaste: true,
  clipboardShortcuts: 'auto' as const
};

// =============================================================================
// WebSocket Configuration
// =============================================================================

/** Initial reconnect delay in milliseconds */
export const INITIAL_RECONNECT_DELAY = 1000;

/** Maximum reconnect delay in milliseconds */
export const MAX_RECONNECT_DELAY = 30000;
