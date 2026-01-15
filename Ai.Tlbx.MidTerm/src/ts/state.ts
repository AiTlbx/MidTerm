/**
 * Application State
 *
 * Ephemeral state that doesn't need reactivity - WebSocket instances,
 * terminal Maps, DOM cache, etc. Reactive state lives in stores/index.ts.
 */

import type { TerminalState, Settings, UpdateInfo, AuthStatus, DOMElements } from './types';

// =============================================================================
// Server Data State (NOT migrated - currentSettings used for terminal options)
// =============================================================================

/** User settings from server */
export let currentSettings: Settings | null = null;

/** Update info from server */
export let updateInfo: UpdateInfo | null = null;

/** Auth status from server */
export let authStatus: AuthStatus | null = null;

// =============================================================================
// WebSocket State
// =============================================================================

/** State WebSocket connection */
export let stateWs: WebSocket | null = null;

/** State WebSocket reconnect timer */
export let stateReconnectTimer: number | undefined;

/** Mux WebSocket connection */
export let muxWs: WebSocket | null = null;

/** Mux WebSocket reconnect timer */
export let muxReconnectTimer: number | undefined;

// =============================================================================
// Terminal State
// =============================================================================

/** Windows build number for ConPTY configuration (null on non-Windows) */
export let windowsBuildNumber: number | null = null;

/** Per-session terminal state */
export const sessionTerminals = new Map<string, TerminalState>();

/** Sessions created in this browser session (use WebSocket buffering) */
export const newlyCreatedSessions = new Set<string>();

/** Pending sessions being created (for optimistic UI) */
export const pendingSessions = new Set<string>();

/** Buffer WebSocket output frames for terminals not yet opened */
export const pendingOutputFrames = new Map<string, Uint8Array[]>();

/** Sessions that overflowed pending frames and need full resync when opened */
export const sessionsNeedingResync = new Set<string>();

/** Font loading promise */
export let fontsReadyPromise: Promise<void> | null = null;

// =============================================================================
// DOM Element Cache
// =============================================================================

/** Cached DOM elements */
export const dom: DOMElements = {
  sessionList: null,
  sessionCount: null,
  terminalsArea: null,
  emptyState: null,
  mobileTitle: null,
  topbarActions: null,
  app: null,
  sidebarOverlay: null,
  settingsView: null,
  settingsBtn: null,
  titleBarCustom: null,
  titleBarTerminal: null,
  titleBarSeparator: null,
};

// =============================================================================
// State Setters
// =============================================================================

export function setCurrentSettings(settings: Settings | null): void {
  currentSettings = settings;
}

export function setUpdateInfo(info: UpdateInfo | null): void {
  updateInfo = info;
}

export function setAuthStatus(status: AuthStatus | null): void {
  authStatus = status;
}

export function setStateWs(ws: WebSocket | null): void {
  stateWs = ws;
}

export function setStateReconnectTimer(timer: number | undefined): void {
  stateReconnectTimer = timer;
}

export function setMuxWs(ws: WebSocket | null): void {
  muxWs = ws;
}

export function setMuxReconnectTimer(timer: number | undefined): void {
  muxReconnectTimer = timer;
}

export function setFontsReadyPromise(promise: Promise<void>): void {
  fontsReadyPromise = promise;
}

export function setWindowsBuildNumber(build: number | null): void {
  windowsBuildNumber = build;
}

// =============================================================================
// DOM Element Cache Initialization
// =============================================================================

/**
 * Cache DOM elements for quick access
 */
export function cacheDOMElements(): void {
  dom.sessionList = document.getElementById('session-list');
  dom.sessionCount = document.getElementById('session-count');
  dom.terminalsArea = document.querySelector('.terminals-area');
  dom.emptyState = document.getElementById('empty-state');
  dom.mobileTitle = document.getElementById('mobile-title');
  dom.topbarActions = document.getElementById('topbar-actions');
  dom.app = document.getElementById('app');
  dom.sidebarOverlay = document.getElementById('sidebar-overlay');
  dom.settingsView = document.getElementById('settings-view');
  dom.settingsBtn = document.getElementById('btn-settings');
  dom.titleBarCustom = document.getElementById('title-bar-custom');
  dom.titleBarTerminal = document.getElementById('title-bar-terminal');
  dom.titleBarSeparator = document.getElementById('title-bar-separator');
}
