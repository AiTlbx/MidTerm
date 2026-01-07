/**
 * File Drop Module
 *
 * Handles drag-and-drop file uploads and clipboard image paste.
 * Files are uploaded to the server and the resulting path is inserted into the terminal.
 */

import { activeSessionId } from '../../state';

// Forward declarations for callbacks
let pasteToTerminal: (sessionId: string, data: string, isFilePath?: boolean) => void = () => {};

/**
 * Sanitize pasted content to:
 * 1. Normalize line endings (CRLF/CR → LF) to prevent interleaved empty lines
 * 2. Strip all escape sequences to prevent "appears then deleted" bugs
 * 3. Remove BPM markers to prevent paste escape attacks
 *
 * BPM markers are re-added by pasteToTerminal() after sanitization.
 */
function sanitizePasteContent(text: string): string {
  return text
    .replace(/\r\n/g, '\n')                     // Normalize CRLF → LF first
    .replace(/\r(?!\n)/g, '\n')                 // Normalize CR → LF (Mac Classic)
    .replace(/\x1b\[[0-9;]*[A-Za-z]/g, '')      // Remove CSI sequences (colors, cursor, clear)
    .replace(/\x1b\][^\x07]*\x07/g, '')         // Remove OSC sequences (titles, hyperlinks)
    .replace(/\x1b[PX^_][^\x1b]*\x1b\\/g, '')   // Remove DCS/SOS/PM/APC sequences
    .replace(/\x1b[\x20-\x2F]*[\x30-\x7E]/g, ''); // Remove other escape sequences
}

/**
 * Register callbacks from mux channel and terminal manager
 */
export function registerFileDropCallbacks(callbacks: {
  sendInput?: (sessionId: string, data: string) => void;
  pasteToTerminal?: (sessionId: string, data: string, isFilePath?: boolean) => void;
}): void {
  if (callbacks.pasteToTerminal) pasteToTerminal = callbacks.pasteToTerminal;
}

/**
 * Upload a file to the server for the given session
 */
async function uploadFile(sessionId: string, file: File): Promise<string | null> {
  const formData = new FormData();
  formData.append('file', file);

  try {
    const response = await fetch(`/api/sessions/${sessionId}/upload`, {
      method: 'POST',
      body: formData
    });

    if (!response.ok) {
      console.error('File upload failed:', response.status);
      return null;
    }

    const result = await response.json();
    return result.path;
  } catch (error) {
    console.error('File upload error:', error);
    return null;
  }
}

/**
 * Handle file drop - upload and insert path
 */
async function handleFileDrop(files: FileList): Promise<void> {
  if (!activeSessionId || files.length === 0) return;

  const paths: string[] = [];

  for (const file of Array.from(files)) {
    const path = await uploadFile(activeSessionId, file);
    if (path) {
      paths.push(path);
    }
  }

  if (paths.length > 0) {
    const joined = sanitizePasteContent(paths.join(' '));
    pasteToTerminal(activeSessionId, joined, true);
  }
}

/**
 * Set up drag-and-drop handlers for a terminal container
 */
export function setupFileDrop(container: HTMLElement): void {
  // Prevent default drag behaviors
  container.addEventListener('dragover', (e) => {
    e.preventDefault();
    e.stopPropagation();
    container.classList.add('drag-over');
  });

  container.addEventListener('dragleave', (e) => {
    e.preventDefault();
    e.stopPropagation();
    container.classList.remove('drag-over');
  });

  container.addEventListener('dragend', () => {
    container.classList.remove('drag-over');
  });

  // Handle drop
  container.addEventListener('drop', async (e) => {
    e.preventDefault();
    e.stopPropagation();
    container.classList.remove('drag-over');

    const files = e.dataTransfer?.files;
    if (files && files.length > 0) {
      await handleFileDrop(files);
    }
  });
}

/**
 * Handle clipboard paste - checks for images first, falls back to text
 * Used by the keyboard handler in manager.ts
 */
export async function handleClipboardPaste(sessionId: string): Promise<void> {
  // Try to read clipboard items (images)
  try {
    const items = await navigator.clipboard.read();
    for (const item of items) {
      const imageType = item.types.find(t => t.startsWith('image/'));
      if (imageType) {
        const blob = await item.getType(imageType);
        const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
        const file = new File([blob], `clipboard_${timestamp}.jpg`, { type: imageType });
        const path = await uploadFile(sessionId, file);
        if (path) {
          pasteToTerminal(sessionId, sanitizePasteContent(path), true);
          return; // Image handled, don't paste text
        }
      }
    }
  } catch {
    // clipboard.read() not supported or failed, fall through to text paste
  }

  // No image found or image handling failed, paste text
  try {
    const text = await navigator.clipboard.readText();
    if (text) {
      const sanitized = sanitizePasteContent(text);
      pasteToTerminal(sessionId, sanitized);
    }
  } catch {
    // Text paste failed
  }
}
