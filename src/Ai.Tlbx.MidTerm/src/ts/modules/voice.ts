/**
 * Voice Module
 *
 * Handles WebSocket connection to MidTerm.Voice server
 * and bridges audio capture/playback.
 */

import { createLogger } from './logging';
import {
  setVoiceStatus,
  setMicActive,
  setToggleEnabled,
  setToggleRecording,
} from './sidebar/voiceSection';

const log = createLogger('voice');
const VOICE_SERVER_PORT = 2010;

let ws: WebSocket | null = null;
let isSessionActive = false;
let voiceServerAvailable = false;
let audioFrameCount = 0;
let totalBytesSent = 0;

/**
 * Check if MidTerm.Voice server is available
 */
export async function checkVoiceServerHealth(): Promise<boolean> {
  try {
    const host = window.location.hostname;
    const url = `http://${host}:${VOICE_SERVER_PORT}/api/health`;

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 2000);

    const response = await fetch(url, { signal: controller.signal });
    clearTimeout(timeoutId);

    if (response.ok) {
      const data = await response.json();
      voiceServerAvailable = data.status === 'ok';
      log.info(() => `Voice server available: v${data.version}`);
      return voiceServerAvailable;
    }
  } catch {
    // Server not available - this is expected if not running
    log.info(() => 'Voice server not available');
  }
  voiceServerAvailable = false;
  return false;
}

/**
 * Get current voice server availability status
 */
export function isVoiceServerAvailable(): boolean {
  return voiceServerAvailable;
}

/**
 * Request microphone permission and initialize audio
 */
export async function requestMicrophonePermission(): Promise<boolean> {
  try {
    log.info(() => 'Requesting microphone permission');

    if (!window.initAudioWithUserInteraction) {
      log.error(() => 'Audio API not available');
      setVoiceStatus('Audio API not available');
      return false;
    }

    const result = await window.initAudioWithUserInteraction();
    if (!result) {
      setVoiceStatus('Audio init failed');
      return false;
    }

    if (window.requestMicrophonePermissionAndGetDevices) {
      await window.requestMicrophonePermissionAndGetDevices();
    }

    log.info(() => 'Microphone permission granted');
    setVoiceStatus('Ready');
    return true;
  } catch (error) {
    log.error(() => `Microphone permission error: ${error}`);
    setVoiceStatus('Mic permission denied');
    return false;
  }
}

/**
 * Start a voice session - connect to MidTerm.Voice and begin recording
 */
export async function startVoiceSession(): Promise<void> {
  if (isSessionActive) {
    log.warn(() => 'Voice session already active');
    return;
  }

  try {
    const host = window.location.hostname;
    const wsUrl = `ws://${host}:${VOICE_SERVER_PORT}/voice`;

    // Reset counters
    audioFrameCount = 0;
    totalBytesSent = 0;

    log.info(() => `[WS] Connecting to ${wsUrl}`);
    setVoiceStatus('Connecting...');

    ws = new WebSocket(wsUrl);

    ws.onopen = async () => {
      log.info(() => '[WS] Connected, sending start command');
      setVoiceStatus('Connected');

      // Send start message
      const startMsg = JSON.stringify({ type: 'start' });
      ws?.send(startMsg);
      log.info(() => `[WS] Sent: ${startMsg}`);

      // Start recording
      if (window.startRecording) {
        log.info(() => '[AUDIO] Calling startRecording(callback, 500ms, null, 24000Hz)');
        const success = await window.startRecording(
          (base64Audio: string) => {
            if (ws && ws.readyState === WebSocket.OPEN) {
              // Convert base64 to ArrayBuffer and send
              const bytes = base64ToArrayBuffer(base64Audio);
              audioFrameCount++;
              totalBytesSent += bytes.byteLength;
              log.info(
                () =>
                  `[AUDIO] Frame #${audioFrameCount}: ${base64Audio.length} chars -> ${bytes.byteLength} bytes (total: ${totalBytesSent})`,
              );
              ws.send(bytes);
            } else {
              log.warn(() => `[AUDIO] Frame dropped - WS not open (readyState: ${ws?.readyState})`);
            }
          },
          500,
          null,
          24000,
        );

        log.info(() => `[AUDIO] startRecording returned: ${success}`);

        if (success) {
          isSessionActive = true;
          setVoiceStatus('Listening...');
          setToggleRecording(true);
          log.info(() => '[SESSION] Voice session active');
        } else {
          log.error(() => '[AUDIO] Recording failed to start');
          setVoiceStatus('Recording failed');
          ws?.close();
        }
      } else {
        log.error(() => '[AUDIO] window.startRecording not available');
      }
    };

    ws.onmessage = async (event: MessageEvent) => {
      if (event.data instanceof Blob) {
        // Audio data from server
        const arrayBuffer = await event.data.arrayBuffer();
        log.info(() => `[WS] Received audio blob: ${arrayBuffer.byteLength} bytes`);
        const base64 = arrayBufferToBase64(arrayBuffer);

        if (window.playAudio) {
          await window.playAudio(base64, 24000);
        }
      } else if (typeof event.data === 'string') {
        log.info(() => `[WS] Received text: ${event.data}`);
        // JSON message
        try {
          const msg = JSON.parse(event.data);
          handleVoiceMessage(msg);
        } catch {
          log.warn(() => `[WS] Invalid JSON from voice server: ${event.data}`);
        }
      }
    };

    ws.onclose = (event: CloseEvent) => {
      log.info(
        () => `[WS] Closed: code=${event.code} reason="${event.reason}" clean=${event.wasClean}`,
      );
      log.info(() => `[SESSION] Stats: ${audioFrameCount} frames, ${totalBytesSent} bytes sent`);
      isSessionActive = false;
      setVoiceStatus('Disconnected');
      setToggleRecording(false);
    };

    ws.onerror = () => {
      log.error(() => '[WS] WebSocket error occurred');
      setVoiceStatus('Connection error');
    };
  } catch (error) {
    log.error(() => `[SESSION] Failed to start: ${error}`);
    setVoiceStatus('Connection failed');
  }
}

/**
 * Stop the voice session
 */
export async function stopVoiceSession(): Promise<void> {
  if (!isSessionActive) {
    log.info(() => '[SESSION] Stop called but session not active');
    return;
  }

  log.info(() => '[SESSION] Stopping voice session...');

  // Stop recording
  if (window.stopRecording) {
    log.info(() => '[AUDIO] Calling stopRecording()');
    await window.stopRecording();
    log.info(() => '[AUDIO] stopRecording() completed');
  }

  // Stop playback
  if (window.stopAudioPlayback) {
    log.info(() => '[AUDIO] Calling stopAudioPlayback()');
    await window.stopAudioPlayback();
  }

  // Send stop message and close WebSocket
  if (ws && ws.readyState === WebSocket.OPEN) {
    const stopMsg = JSON.stringify({ type: 'stop' });
    log.info(() => `[WS] Sending: ${stopMsg}`);
    ws.send(stopMsg);
    log.info(() => '[WS] Closing WebSocket');
    ws.close();
  }

  isSessionActive = false;
  setVoiceStatus('Ready');
  setToggleRecording(false);
  log.info(() => '[SESSION] Voice session stopped');
}

/**
 * Handle messages from the voice server
 */
function handleVoiceMessage(msg: { type: string; status?: string; message?: string }): void {
  log.info(() => `[MSG] Handling: type=${msg.type} status=${msg.status} message=${msg.message}`);
  switch (msg.type) {
    case 'status':
      if (msg.status) {
        setVoiceStatus(msg.status);
      }
      break;
    case 'speaking':
      setVoiceStatus('Speaking...');
      break;
    case 'listening':
      setVoiceStatus('Listening...');
      break;
    case 'error':
      log.error(() => `[MSG] Server error: ${msg.message || 'unknown'}`);
      setVoiceStatus('Server error');
      break;
    default:
      log.warn(() => `[MSG] Unknown message type: ${msg.type}`);
  }
}

/**
 * Convert base64 string to ArrayBuffer
 */
function base64ToArrayBuffer(base64: string): ArrayBuffer {
  const binaryString = atob(base64);
  const bytes = new Uint8Array(binaryString.length);
  for (let i = 0; i < binaryString.length; i++) {
    bytes[i] = binaryString.charCodeAt(i);
  }
  return bytes.buffer;
}

/**
 * Convert ArrayBuffer to base64 string
 */
function arrayBufferToBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i]!);
  }
  return btoa(binary);
}

/**
 * Bind voice button event handlers
 */
export function bindVoiceEvents(): void {
  const micBtn = document.getElementById('btn-voice-mic');
  const toggleBtn = document.getElementById('btn-voice-toggle');

  log.info(() => `[INIT] Binding voice events: micBtn=${!!micBtn} toggleBtn=${!!toggleBtn}`);

  if (micBtn) {
    micBtn.addEventListener('click', async () => {
      log.info(() => '[UI] Mic button clicked');
      const success = await requestMicrophonePermission();
      log.info(() => `[UI] Mic permission result: ${success}`);
      if (success) {
        setMicActive(true);
        setToggleEnabled(true);
      }
    });
  }

  if (toggleBtn) {
    toggleBtn.addEventListener('click', async () => {
      log.info(() => `[UI] Toggle button clicked (isSessionActive=${isSessionActive})`);
      if (isSessionActive) {
        await stopVoiceSession();
      } else {
        await startVoiceSession();
      }
    });
  }

  // Set up error callback
  if (window.setOnError) {
    window.setOnError((error: string) => {
      log.error(() => `[AUDIO] Error callback: ${error}`);
      setVoiceStatus('Error');
    });
  }

  // Set up recording state callback
  if (window.setOnRecordingState) {
    window.setOnRecordingState((isRecording: boolean) => {
      log.info(() => `[AUDIO] Recording state changed: ${isRecording}`);
    });
  }
}
