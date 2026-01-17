/**
 * Chat Module
 *
 * Handles the voice chat panel display, message rendering,
 * and panel visibility state.
 */

import { createLogger } from '../logging';
import type { ChatMessage } from '../../types';

const log = createLogger('chat');
const STORAGE_KEY = 'midterm.chatPanelOpen';

let chatMessages: ChatMessage[] = [];

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
