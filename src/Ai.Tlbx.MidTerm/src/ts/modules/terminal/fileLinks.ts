/**
 * File Links Module
 *
 * Detects file paths in terminal output and makes them clickable.
 * Uses xterm-link-provider for robust link detection and rendering.
 * Clicking opens the file viewer modal.
 */

/* =============================================================================
 * PERFORMANCE-SENSITIVE CODE - FILE PATH DETECTION
 * =============================================================================
 *
 * This module scans ALL terminal output for file paths. It runs on every frame
 * of terminal data, which can be frequent during:
 *   - TUI apps (vim, htop, less) with rapid redraws
 *   - Large file outputs (cat, logs, builds)
 *   - High-frequency updates (tail -f, watch)
 *
 * OPTIMIZATIONS APPLIED:
 *   1. Reused TextDecoder instance (avoid allocation per frame)
 *   2. Minimum frame size threshold (skip tiny cursor-move frames)
 *   3. Debounced scanning (batch rapid frames, run in idle time)
 *   4. Reused regex patterns (no new RegExp per call)
 *   5. Early bailout for frames with no path-like characters
 *
 * IF UI PERFORMANCE DEGRADES:
 *   - Increase MIN_SCAN_FRAME_SIZE to skip more small frames
 *   - Increase SCAN_DEBOUNCE_MS to batch more aggressively
 *   - Disable via Settings > Behavior > "File Radar (experimental)"
 *   - Or comment out scanOutputForPaths() call in manager.ts
 *
 * =========================================================================== */

import type { Terminal } from '@xterm/xterm';
import { LinkProvider } from 'xterm-link-provider';
import type { FilePathInfo, FileCheckResponse } from '../../types';
import { openFile } from '../fileViewer';
import { createLogger } from '../logging';
import { currentSettings } from '../../state';

const log = createLogger('fileLinks');

// ===========================================================================
// PERFORMANCE TUNING CONSTANTS - Adjust these if performance degrades
// ===========================================================================

/**
 * Check if File Radar is enabled via settings.
 * Controlled by Settings > Behavior > "File Radar (experimental)"
 * Default: OFF - user must explicitly enable this feature.
 */
function isFileRadarEnabled(): boolean {
  return currentSettings?.fileRadar === true;
}

/** Minimum frame size in bytes to bother scanning (skip tiny cursor moves) */
const MIN_SCAN_FRAME_SIZE = 8;

/** Debounce interval for batching rapid terminal output (ms) */
const SCAN_DEBOUNCE_MS = 50;

/** Maximum paths to track per session (FIFO eviction) */
const MAX_ALLOWLIST_SIZE = 1000;

/** Cache TTL for file existence checks (ms) */
const EXISTENCE_CACHE_TTL = 30000;

/** Quick check: does the text contain characters that could be a path? */
const QUICK_PATH_CHECK_UNIX = /\//;
const QUICK_PATH_CHECK_WIN = /[A-Za-z]:/;

// ===========================================================================
// Module State
// ===========================================================================

const pathAllowlists = new Map<string, Set<string>>();
const existenceCache = new Map<string, { info: FilePathInfo | null; expires: number }>();

/** Reused TextDecoder to avoid allocation per frame */
const textDecoder = new TextDecoder();

/** Pending text to scan per session (for debouncing) */
const pendingScanText = new Map<string, string>();

/** Debounce timers per session */
const scanTimers = new Map<string, number>();

// ===========================================================================
// Regex Patterns - Compiled once at module load
// ===========================================================================

/**
 * Unix absolute paths: /path/to/file or /path/to/file.ext
 * Pattern for xterm-link-provider (uses capture group 1 for the link text)
 */
const UNIX_PATH_PATTERN = /(\/(?:[\w.-]+\/)*[\w.-]+(?:\.\w+)?)/;

/**
 * Windows absolute paths: C:\path\file or C:/path/file
 * Pattern for xterm-link-provider (uses capture group 1 for the link text)
 */
const WIN_PATH_PATTERN = /([A-Za-z]:[\\/](?:[\w.-]+[\\/])*[\w.-]+(?:\.\w+)?)/;

/**
 * Global versions for scanning terminal output
 */
const UNIX_PATH_PATTERN_GLOBAL = /(?:^|[\s"'`(])(\/([\w.-]+\/)*[\w.-]+(?:\.\w+)?)(?=[\s"'`)]|$)/g;
const WIN_PATH_PATTERN_GLOBAL =
  /(?:^|[\s"'`(])([A-Za-z]:[\\/](?:[\w.-]+[\\/])*[\w.-]+(?:\.\w+)?)(?=[\s"'`)]|$)/g;

// ===========================================================================
// Toast Notification
// ===========================================================================

function showFileNotFoundToast(path: string): void {
  const existing = document.querySelector('.drop-toast');
  if (existing) existing.remove();

  const toast = document.createElement('div');
  toast.className = 'drop-toast error';
  toast.textContent = `File not found: ${path}`;
  document.body.appendChild(toast);

  setTimeout(() => {
    toast.classList.add('hiding');
    setTimeout(() => toast.remove(), 300);
  }, 3000);
}

// ===========================================================================
// Public API
// ===========================================================================

export function getPathAllowlist(sessionId: string): Set<string> {
  let allowlist = pathAllowlists.get(sessionId);
  if (!allowlist) {
    allowlist = new Set();
    pathAllowlists.set(sessionId, allowlist);
  }
  return allowlist;
}

export function clearPathAllowlist(sessionId: string): void {
  pathAllowlists.delete(sessionId);
  // Also clear any pending scan
  const timer = scanTimers.get(sessionId);
  if (timer) {
    window.clearTimeout(timer);
    scanTimers.delete(sessionId);
  }
  pendingScanText.delete(sessionId);
}

/**
 * Queue terminal output for path scanning.
 * Debounced to batch rapid frames and reduce CPU overhead.
 *
 * PERFORMANCE NOTE: This is called on EVERY terminal output frame.
 * Keep this function as fast as possible - actual scanning is deferred.
 */
export function scanOutputForPaths(sessionId: string, data: string | Uint8Array): void {
  if (!isFileRadarEnabled()) {
    return;
  }

  // Decode if needed (reuse decoder to avoid allocation)
  const text = typeof data === 'string' ? data : textDecoder.decode(data);

  // Skip tiny frames (likely cursor moves, not real content)
  if (text.length < MIN_SCAN_FRAME_SIZE) return;

  // Quick check: does this text even contain path-like characters?
  // This avoids regex overhead for frames that are clearly not paths
  if (!QUICK_PATH_CHECK_UNIX.test(text) && !QUICK_PATH_CHECK_WIN.test(text)) {
    return;
  }

  // Append to pending text for this session
  const existing = pendingScanText.get(sessionId) || '';
  pendingScanText.set(sessionId, existing + text);

  // Debounce: reset timer and schedule scan
  const existingTimer = scanTimers.get(sessionId);
  if (existingTimer) {
    window.clearTimeout(existingTimer);
  }

  const timer = window.setTimeout(() => {
    scanTimers.delete(sessionId);
    const pendingText = pendingScanText.get(sessionId);
    pendingScanText.delete(sessionId);
    if (pendingText) {
      performScan(sessionId, pendingText);
    }
  }, SCAN_DEBOUNCE_MS);

  scanTimers.set(sessionId, timer);
}

// ===========================================================================
// Internal Implementation
// ===========================================================================

/**
 * Actually perform the regex scan on accumulated text.
 * Called after debounce delay, potentially in idle time.
 * This pre-caches file existence for faster click response.
 */
function performScan(sessionId: string, text: string): void {
  // Strip ANSI escape sequences before regex matching
  /* eslint-disable no-control-regex */
  const cleanText = text
    .replace(/\x1b\[[0-9;?]*[A-Za-z]/g, '') // CSI sequences
    .replace(/\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)/g, '') // OSC sequences
    .replace(/\x1b\][^\x07]*/g, ''); // Incomplete OSC
  /* eslint-enable no-control-regex */

  const allowlist = getPathAllowlist(sessionId);

  // Reset regex lastIndex and scan for Unix paths
  UNIX_PATH_PATTERN_GLOBAL.lastIndex = 0;
  for (const match of cleanText.matchAll(UNIX_PATH_PATTERN_GLOBAL)) {
    const path = match[1];
    if (!path) continue;
    if (isValidPath(path)) {
      addToAllowlist(allowlist, path);
      // Pre-cache existence check
      checkPathExists(path);
    }
  }

  // Reset regex lastIndex and scan for Windows paths
  WIN_PATH_PATTERN_GLOBAL.lastIndex = 0;
  for (const match of cleanText.matchAll(WIN_PATH_PATTERN_GLOBAL)) {
    const path = match[1];
    if (!path) continue;
    if (isValidPath(path)) {
      addToAllowlist(allowlist, path);
      // Pre-cache existence check
      checkPathExists(path);
    }
  }
}

function addToAllowlist(allowlist: Set<string>, path: string): void {
  if (allowlist.size >= MAX_ALLOWLIST_SIZE) {
    // FIFO eviction - remove oldest entry
    const firstKey = allowlist.values().next().value;
    if (firstKey) allowlist.delete(firstKey);
  }
  allowlist.add(path);
}

function isValidPath(path: string): boolean {
  if (!path || path.length < 2) return false;
  if (path.includes('..')) return false;
  // Reject simple Unix commands that look like paths (e.g., /bin, /usr)
  if (/^\/[a-z]+$/.test(path)) return false;
  return true;
}

async function checkPathExists(path: string): Promise<FilePathInfo | null> {
  const cached = existenceCache.get(path);
  if (cached && cached.expires > Date.now()) {
    return cached.info;
  }

  try {
    const resp = await fetch('/api/files/check', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ paths: [path] }),
    });

    if (!resp.ok) {
      existenceCache.set(path, { info: null, expires: Date.now() + EXISTENCE_CACHE_TTL });
      return null;
    }

    const data: FileCheckResponse = await resp.json();
    const info = data.results[path] || null;

    existenceCache.set(path, { info, expires: Date.now() + EXISTENCE_CACHE_TTL });
    return info;
  } catch (e) {
    log.error(() => `Failed to check path existence: ${e}`);
    return null;
  }
}

// ===========================================================================
// Click Handler
// ===========================================================================

async function handlePathClick(path: string): Promise<void> {
  const info = await checkPathExists(path);
  if (info?.exists) {
    openFile(path, info);
  } else {
    showFileNotFoundToast(path);
  }
}

// ===========================================================================
// Link Provider Registration
// ===========================================================================

/**
 * Register the file link provider with xterm.js using xterm-link-provider.
 * This is called once per terminal session.
 */
export function registerFileLinkProvider(terminal: Terminal, _sessionId: string): void {
  if (!isFileRadarEnabled()) return;

  // Cast to 'any' because xterm-link-provider was built for xterm 4.x
  // but we use @xterm/xterm 5+. The APIs are compatible at runtime.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const term = terminal as any;

  // Unix paths
  terminal.registerLinkProvider(
    new LinkProvider(term, UNIX_PATH_PATTERN, async (_event, path) => {
      await handlePathClick(path);
    }),
  );

  // Windows paths
  terminal.registerLinkProvider(
    new LinkProvider(term, WIN_PATH_PATTERN, async (_event, path) => {
      await handlePathClick(path);
    }),
  );

  log.verbose(() => `Registered file link provider`);
}

// Legacy export for compatibility (unused but keeps API stable)
export function createFileLinkProvider(_sessionId: string): null {
  return null;
}
