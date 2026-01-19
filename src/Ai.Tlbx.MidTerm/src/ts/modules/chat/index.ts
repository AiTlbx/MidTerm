/**
 * Chat Module
 *
 * Handles the voice chat panel display, message rendering,
 * and panel visibility state.
 */

import { createLogger } from '../logging';
import type { ChatMessage, VoiceToolName, InteractiveOp } from '../../types';

const log = createLogger('chat');
const STORAGE_KEY = 'midterm.chatPanelOpen';

let chatMessages: ChatMessage[] = [];
let autoAcceptEnabled = false;

/**
 * Initialize the chat panel and event handlers
 */
export function initChatPanel(): void {
  const collapseBtn = document.getElementById('btn-collapse-chat');
  if (collapseBtn) {
    collapseBtn.addEventListener('click', hideChatPanel);
  }

  log.info(() => 'Chat panel initialized');
}

/**
 * Show the chat panel
 */
export function showChatPanel(): void {
  const panel = document.getElementById('chat-panel');
  if (panel) {
    panel.classList.remove('hidden');
    localStorage.setItem(STORAGE_KEY, 'true');
    log.info(() => 'Chat panel shown');
  }
}

/**
 * Hide the chat panel
 */
export function hideChatPanel(): void {
  const panel = document.getElementById('chat-panel');
  if (panel) {
    panel.classList.add('hidden');
    localStorage.setItem(STORAGE_KEY, 'false');
    log.info(() => 'Chat panel hidden');
  }
}

/**
 * Toggle the chat panel visibility
 */
export function toggleChatPanel(): void {
  const panel = document.getElementById('chat-panel');
  if (panel?.classList.contains('hidden')) {
    showChatPanel();
  } else {
    hideChatPanel();
  }
}

/**
 * Check if the chat panel is currently visible
 */
export function isChatPanelVisible(): boolean {
  const panel = document.getElementById('chat-panel');
  return panel !== null && !panel.classList.contains('hidden');
}

/**
 * Restore chat panel state from localStorage
 */
export function restoreChatPanelState(): void {
  const wasOpen = localStorage.getItem(STORAGE_KEY) === 'true';
  if (wasOpen) {
    showChatPanel();
  }
}

/**
 * Add a chat message and render it
 */
export function addChatMessage(message: ChatMessage): void {
  chatMessages.push(message);
  renderMessage(message);
  scrollToBottom();
  log.info(() => `Chat message added: ${message.role}`);
}

/**
 * Clear all chat messages
 */
export function clearChatMessages(): void {
  chatMessages = [];
  const container = document.getElementById('chat-messages');
  if (container) {
    container.innerHTML = '';
  }
  log.info(() => 'Chat messages cleared');
}

/**
 * Get all chat messages
 */
export function getChatMessages(): ChatMessage[] {
  return [...chatMessages];
}

/**
 * Render a single message to the chat panel
 */
function renderMessage(message: ChatMessage): void {
  const container = document.getElementById('chat-messages');
  if (!container) return;

  const msgEl = document.createElement('div');
  msgEl.className = `chat-msg chat-msg-${message.role}`;

  if (message.role === 'tool') {
    msgEl.innerHTML = `
      <div class="chat-msg-tool-name">${escapeHtml(message.toolName || 'tool')}</div>
      <div class="chat-msg-tool-result">${escapeHtml(message.content)}</div>
    `;
  } else {
    const time = formatTime(message.timestamp);
    msgEl.innerHTML = `
      <div class="chat-msg-content">${escapeHtml(message.content)}</div>
      <div class="chat-msg-time">${time}</div>
    `;
  }

  container.appendChild(msgEl);
}

/**
 * Scroll the chat messages to the bottom
 */
function scrollToBottom(): void {
  const container = document.getElementById('chat-messages');
  if (container) {
    container.scrollTop = container.scrollHeight;
  }
}

/**
 * Format ISO timestamp to time string (HH:MM)
 */
function formatTime(timestamp: string): string {
  try {
    const date = new Date(timestamp);
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  } catch {
    return '';
  }
}

/**
 * Escape HTML to prevent XSS
 */
function escapeHtml(text: string): string {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

/**
 * Check if auto-accept is enabled for this session
 */
export function isAutoAcceptEnabled(): boolean {
  return autoAcceptEnabled;
}

/**
 * Format a tool request for display
 */
function formatToolDisplay(tool: VoiceToolName, args: Record<string, unknown>): string {
  switch (tool) {
    case 'make_input': {
      const text = (args.text as string) || '';
      const formatted = formatInputText(text);
      return `Send to terminal:\n${formatted}`;
    }
    case 'interactive_read': {
      const ops = (args.operations as InteractiveOp[]) || [];
      const lines = ops.map((op, i) => {
        if (op.type === 'input') {
          return `${i + 1}. Input: ${formatInputText(op.data || '')}`;
        } else if (op.type === 'delay') {
          return `${i + 1}. Wait ${op.delayMs || 100}ms`;
        } else {
          return `${i + 1}. Screenshot`;
        }
      });
      return `Interactive sequence:\n${lines.join('\n')}`;
    }
    default:
      return `Tool: ${tool}`;
  }
}

/**
 * Format input text with escape sequence visualization
 */
function formatInputText(text: string): string {
  // Use string literals instead of regex for control characters
  let result = text;
  result = result.split('\r').join('⏎');
  result = result.split('\n').join('↵');
  result = result.split('\t').join('⇥');
  result = result.split(String.fromCharCode(3)).join('^C'); // Ctrl+C
  result = result.split(String.fromCharCode(27) + '[A').join('↑'); // Arrow Up
  result = result.split(String.fromCharCode(27) + '[B').join('↓'); // Arrow Down
  result = result.split(String.fromCharCode(27) + '[C').join('→'); // Arrow Right
  result = result.split(String.fromCharCode(27) + '[D').join('←'); // Arrow Left
  result = result.split(String.fromCharCode(27) + '[5~').join('⇞'); // Page Up
  result = result.split(String.fromCharCode(27) + '[6~').join('⇟'); // Page Down
  result = result.split(String.fromCharCode(27)).join('ESC'); // Remaining ESC
  return result;
}

/**
 * Show a tool confirmation dialog in the chat panel
 * Returns true if approved, false if declined
 */
export function showToolConfirmation(
  tool: VoiceToolName,
  args: Record<string, unknown>,
  justification: string | undefined,
): Promise<boolean> {
  // If auto-accept is enabled, return immediately
  if (autoAcceptEnabled) {
    log.info(() => `Auto-accepting tool: ${tool}`);
    return Promise.resolve(true);
  }

  return new Promise((resolve) => {
    const container = document.getElementById('chat-messages');
    if (!container) {
      log.warn(() => 'Chat container not found, auto-declining');
      resolve(false);
      return;
    }

    const displayText = formatToolDisplay(tool, args);

    const msgEl = document.createElement('div');
    msgEl.className = 'chat-msg chat-msg-tool-confirm';

    msgEl.innerHTML = `
      <div class="chat-msg-tool-header">
        <span class="chat-msg-tool-icon">⚠️</span>
        <span class="chat-msg-tool-title">Action requires approval</span>
      </div>
      <div class="chat-msg-tool-name">${escapeHtml(tool)}</div>
      <pre class="chat-msg-tool-command">${escapeHtml(displayText)}</pre>
      ${justification ? `<div class="chat-msg-tool-justification">${escapeHtml(justification)}</div>` : ''}
      <div class="chat-msg-tool-actions">
        <button class="btn-tool-accept">Accept</button>
        <button class="btn-tool-decline">Decline</button>
        <label class="tool-auto-accept">
          <input type="checkbox" class="tool-auto-accept-check" />
          Auto-accept this session
        </label>
      </div>
    `;

    container.appendChild(msgEl);
    scrollToBottom();

    // Bind button events
    const acceptBtn = msgEl.querySelector('.btn-tool-accept') as HTMLButtonElement;
    const declineBtn = msgEl.querySelector('.btn-tool-decline') as HTMLButtonElement;
    const autoAcceptCheck = msgEl.querySelector('.tool-auto-accept-check') as HTMLInputElement;

    const handleResponse = (approved: boolean): void => {
      // Check if auto-accept was toggled
      if (autoAcceptCheck.checked) {
        autoAcceptEnabled = true;
        log.info(() => 'Auto-accept enabled for this session');
      }

      // Update UI to show result
      msgEl.classList.add(approved ? 'confirmed' : 'declined');
      const actionsEl = msgEl.querySelector('.chat-msg-tool-actions');
      if (actionsEl) {
        actionsEl.innerHTML = approved
          ? '<span class="tool-status accepted">✓ Accepted</span>'
          : '<span class="tool-status declined">✗ Declined</span>';
      }

      log.info(() => `Tool ${tool} ${approved ? 'accepted' : 'declined'}`);
      resolve(approved);
    };

    acceptBtn.addEventListener('click', () => handleResponse(true));
    declineBtn.addEventListener('click', () => handleResponse(false));
  });
}
