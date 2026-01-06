/**
 * Mux Channel Module
 *
 * Manages the mux WebSocket connection for terminal I/O.
 * Uses a binary protocol with 9-byte header (1 byte type + 8 byte session ID).
 */

import type { TerminalState } from '../../types';
import {
  MUX_HEADER_SIZE,
  MUX_TYPE_OUTPUT,
  MUX_TYPE_INPUT,
  MUX_TYPE_RESIZE,
  MUX_TYPE_RESYNC,
  MUX_TYPE_BUFFER_REQUEST,
  MUX_TYPE_COMPRESSED_OUTPUT,
  INITIAL_RECONNECT_DELAY,
  MAX_RECONNECT_DELAY
} from '../../constants';
import { parseOutputFrame, parseCompressedOutputFrame, scheduleReconnect } from '../../utils';
import {
  muxWs,
  muxReconnectTimer,
  muxReconnectDelay,
  sessionTerminals,
  pendingOutputFrames,
  setMuxWs,
  setMuxReconnectTimer,
  setMuxReconnectDelay,
  setMuxWsConnected
} from '../../state';
import { updateConnectionStatus } from './stateChannel';

// Forward declarations for functions from other modules
let applyTerminalScaling: (sessionId: string, state: TerminalState) => void = () => {};
let refreshActiveTerminalBuffer: () => void = () => {};

/**
 * Register callbacks from other modules
 */
export function registerMuxCallbacks(callbacks: {
  applyTerminalScaling?: (sessionId: string, state: TerminalState) => void;
  refreshActiveTerminalBuffer?: () => void;
}): void {
  if (callbacks.applyTerminalScaling) applyTerminalScaling = callbacks.applyTerminalScaling;
  if (callbacks.refreshActiveTerminalBuffer) refreshActiveTerminalBuffer = callbacks.refreshActiveTerminalBuffer;
}

/**
 * Connect to the mux WebSocket for terminal I/O.
 * Uses a binary protocol with 9-byte header.
 */
export function connectMuxWebSocket(): void {
  // Close existing WebSocket before creating new one
  if (muxWs) {
    muxWs.onclose = null; // Prevent reconnect loop
    muxWs.close();
    setMuxWs(null);
  }

  const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const ws = new WebSocket(`${protocol}//${location.host}/ws/mux`);
  ws.binaryType = 'arraybuffer';
  setMuxWs(ws);

  ws.onopen = () => {
    const wasReconnect = muxReconnectDelay > INITIAL_RECONNECT_DELAY;
    setMuxReconnectDelay(INITIAL_RECONNECT_DELAY);
    setMuxWsConnected(true);
    updateConnectionStatus();

    // On reconnect, refresh buffers to catch any missed output
    if (wasReconnect) {
      refreshActiveTerminalBuffer();
    }
  };

  ws.onmessage = (event) => {
    if (!(event.data instanceof ArrayBuffer)) return;

    const data = new Uint8Array(event.data);
    if (data.length < MUX_HEADER_SIZE) return;

    const type = data[0];
    const sessionId = decodeSessionId(data, 1);
    const payload = data.slice(MUX_HEADER_SIZE);

    if (type === MUX_TYPE_RESYNC) {
      // Server is resyncing due to dropped frames - clear all terminals
      console.log('[Resync] Clearing terminals for buffer refresh');
      sessionTerminals.forEach((state) => {
        if (state.opened) {
          state.terminal.clear();
        }
      });
      pendingOutputFrames.clear();
      return;
    }

    if (type === MUX_TYPE_OUTPUT || type === MUX_TYPE_COMPRESSED_OUTPUT) {
      // Queue ALL output frames (compressed or not) to maintain order
      // This is critical for TUI apps that rely on escape sequence ordering
      queueOutputFrame(sessionId, payload.slice(), type === MUX_TYPE_COMPRESSED_OUTPUT);
    }
  };

  ws.onclose = () => {
    setMuxWsConnected(false);
    updateConnectionStatus();
    scheduleMuxReconnect();
  };

  ws.onerror = (e) => {
    console.error('Mux WebSocket error:', e);
  };
}

/**
 * Send terminal input to server.
 */
export function sendInput(sessionId: string, data: string): void {
  if (!muxWs || muxWs.readyState !== WebSocket.OPEN) return;

  const payload = new TextEncoder().encode(data);
  const frame = new Uint8Array(MUX_HEADER_SIZE + payload.length);
  frame[0] = MUX_TYPE_INPUT;
  encodeSessionId(frame, 1, sessionId);
  frame.set(payload, MUX_HEADER_SIZE);
  muxWs.send(frame);
}

/**
 * Send terminal resize to server.
 */
export function sendResize(sessionId: string, cols: number, rows: number): void {
  if (!muxWs || muxWs.readyState !== WebSocket.OPEN) return;

  const frame = new Uint8Array(MUX_HEADER_SIZE + 4);
  frame[0] = MUX_TYPE_RESIZE;
  encodeSessionId(frame, 1, sessionId);
  // Encode cols and rows as little-endian 16-bit integers
  frame[MUX_HEADER_SIZE] = cols & 0xff;
  frame[MUX_HEADER_SIZE + 1] = (cols >> 8) & 0xff;
  frame[MUX_HEADER_SIZE + 2] = rows & 0xff;
  frame[MUX_HEADER_SIZE + 3] = (rows >> 8) & 0xff;
  muxWs.send(frame);

  // Update local tracking
  const state = sessionTerminals.get(sessionId);
  if (state) {
    state.serverCols = cols;
    state.serverRows = rows;
  }
}

/**
 * Request buffer refresh for a session via WebSocket.
 * This ensures the buffer arrives in-order with other terminal data.
 */
export function requestBufferRefresh(sessionId: string): void {
  if (!muxWs || muxWs.readyState !== WebSocket.OPEN) return;

  const frame = new Uint8Array(MUX_HEADER_SIZE);
  frame[0] = MUX_TYPE_BUFFER_REQUEST;
  encodeSessionId(frame, 1, sessionId);
  muxWs.send(frame);
}

/**
 * Encode 8-character session ID into buffer at offset.
 */
export function encodeSessionId(buffer: Uint8Array, offset: number, sessionId: string): void {
  for (let i = 0; i < 8; i++) {
    buffer[offset + i] = i < sessionId.length ? sessionId.charCodeAt(i) : 0;
  }
}

/**
 * Decode 8-character session ID from buffer at offset.
 */
export function decodeSessionId(buffer: Uint8Array, offset: number): string {
  const chars: string[] = [];
  for (let i = 0; i < 8; i++) {
    const byte = buffer[offset + i];
    if (byte !== undefined && byte !== 0) {
      chars.push(String.fromCharCode(byte));
    }
  }
  return chars.join('');
}

/**
 * Schedule mux WebSocket reconnection with exponential backoff.
 */
export function scheduleMuxReconnect(): void {
  scheduleReconnect(
    muxReconnectDelay,
    MAX_RECONNECT_DELAY,
    connectMuxWebSocket,
    setMuxReconnectDelay,
    setMuxReconnectTimer,
    muxReconnectTimer
  );
}

// =============================================================================
// Unified Output Frame Queue
// =============================================================================

interface OutputFrameItem {
  sessionId: string;
  payload: Uint8Array;
  compressed: boolean;
}

const outputQueue: OutputFrameItem[] = [];
let processingOutput = false;

/**
 * Queue an output frame for processing.
 * ALL frames (compressed or not) go through this queue to maintain order.
 */
function queueOutputFrame(sessionId: string, payload: Uint8Array, compressed: boolean): void {
  outputQueue.push({ sessionId, payload, compressed });
  processOutputQueue();
}

/**
 * Process output frames sequentially to maintain order.
 */
async function processOutputQueue(): Promise<void> {
  if (processingOutput) return;
  processingOutput = true;

  while (outputQueue.length > 0) {
    const item = outputQueue.shift()!;

    try {
      let frame: { cols: number; rows: number; data: Uint8Array; valid: boolean };

      if (item.compressed) {
        frame = await parseCompressedOutputFrame(item.payload);
      } else {
        frame = parseOutputFrame(item.payload);
      }

      if (!frame.valid) {
        console.warn('Invalid output frame for session:', item.sessionId);
        continue;
      }

      const state = sessionTerminals.get(item.sessionId);
      if (state && state.opened) {
        writeFrameToTerminal(item.sessionId, state, frame);
      } else {
        // Buffer for later replay (same format as uncompressed)
        const uncompressedPayload = new Uint8Array(4 + frame.data.length);
        uncompressedPayload[0] = frame.cols & 0xff;
        uncompressedPayload[1] = (frame.cols >> 8) & 0xff;
        uncompressedPayload[2] = frame.rows & 0xff;
        uncompressedPayload[3] = (frame.rows >> 8) & 0xff;
        uncompressedPayload.set(frame.data, 4);

        if (!pendingOutputFrames.has(item.sessionId)) {
          pendingOutputFrames.set(item.sessionId, []);
        }
        pendingOutputFrames.get(item.sessionId)!.push(uncompressedPayload);
      }
    } catch (e) {
      console.error('Failed to process output frame:', e);
    }
  }

  processingOutput = false;
}

/**
 * Write already-parsed frame to terminal.
 */
function writeFrameToTerminal(
  sessionId: string,
  state: TerminalState,
  frame: { cols: number; rows: number; data: Uint8Array; valid: boolean }
): void {
  // Resize terminal if needed
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const termCore = (state.terminal as any)._core;
  if (frame.valid && termCore && termCore._renderService) {
    const currentCols = state.terminal.cols;
    const currentRows = state.terminal.rows;

    if (currentCols !== frame.cols || currentRows !== frame.rows) {
      try {
        state.terminal.resize(frame.cols, frame.rows);
        state.serverCols = frame.cols;
        state.serverRows = frame.rows;
        applyTerminalScaling(sessionId, state);
      } catch (e: unknown) {
        const message = e instanceof Error ? e.message : String(e);
        console.warn('Terminal resize deferred:', message);
      }
    }
  }

  // Write terminal data
  if (frame.data.length > 0) {
    state.terminal.write(frame.data);
  }
}
