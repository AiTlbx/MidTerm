/**
 * Touch Favorites Module
 *
 * User-configurable row of favorite keys.
 * Persisted in localStorage.
 */

import { $activeSessionId } from '../../stores';
import { sendInput } from '../comms/muxChannel';
import { KEY_SEQUENCES, KEY_LABELS } from './constants';
import { consumeModifiers } from './modifiers';

const STORAGE_KEY = 'midterm_touch_favorites';
const DEFAULT_FAVORITES = ['pipe', 'tilde', 'backtick', 'ctrlc', 'ctrld', 'tab'];
const MAX_FAVORITES = 6;

let favoritesContainer: HTMLElement | null = null;
let isEditMode = false;

export function initFavorites(): void {
  favoritesContainer = document.getElementById('touch-favorites');
  if (favoritesContainer) {
    renderFavorites();
    favoritesContainer.addEventListener('click', handleFavoritesClick);
    favoritesContainer.addEventListener('touchend', handleFavoritesTouch);
  }
}

export function teardownFavorites(): void {
  if (favoritesContainer) {
    favoritesContainer.removeEventListener('click', handleFavoritesClick);
    favoritesContainer.removeEventListener('touchend', handleFavoritesTouch);
  }
  favoritesContainer = null;
  isEditMode = false;
}

export function loadFavorites(): string[] {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) {
      const parsed = JSON.parse(stored) as string[];
      if (Array.isArray(parsed)) {
        return parsed.slice(0, MAX_FAVORITES);
      }
    }
  } catch {
    // Ignore parse errors
  }
  return [...DEFAULT_FAVORITES];
}

export function saveFavorites(keys: string[]): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(keys.slice(0, MAX_FAVORITES)));
}

export function addFavorite(key: string): boolean {
  const favorites = loadFavorites();
  if (favorites.length >= MAX_FAVORITES || favorites.includes(key)) {
    return false;
  }
  favorites.push(key);
  saveFavorites(favorites);
  renderFavorites();
  return true;
}

export function removeFavorite(key: string): boolean {
  const favorites = loadFavorites();
  const index = favorites.indexOf(key);
  if (index === -1) {
    return false;
  }
  favorites.splice(index, 1);
  saveFavorites(favorites);
  renderFavorites();
  return true;
}

export function toggleEditMode(): void {
  isEditMode = !isEditMode;
  renderFavorites();
}

export function isInEditMode(): boolean {
  return isEditMode;
}

export function renderFavorites(): void {
  if (!favoritesContainer) return;

  favoritesContainer.innerHTML = '';
  favoritesContainer.classList.toggle('editing', isEditMode);

  const favorites = loadFavorites();

  for (const key of favorites) {
    const wrapper = document.createElement('div');
    wrapper.className = 'touch-fav-wrapper';

    const btn = document.createElement('button');
    btn.className = 'touch-key touch-fav';
    btn.dataset.key = key;
    btn.textContent = KEY_LABELS[key] || key;
    wrapper.appendChild(btn);

    if (isEditMode) {
      const removeBtn = document.createElement('button');
      removeBtn.className = 'touch-fav-remove';
      removeBtn.dataset.removeKey = key;
      removeBtn.innerHTML = '&times;';
      removeBtn.setAttribute('aria-label', `Remove ${KEY_LABELS[key] || key}`);
      wrapper.appendChild(removeBtn);
    }

    favoritesContainer.appendChild(wrapper);
  }

  const editBtn = document.createElement('button');
  editBtn.className = 'touch-key touch-fav-edit';
  editBtn.dataset.action = isEditMode ? 'save-favorites' : 'edit-favorites';
  editBtn.innerHTML = isEditMode ? '&#10003;' : '&#9998;';
  editBtn.setAttribute('aria-label', isEditMode ? 'Save favorites' : 'Edit favorites');
  favoritesContainer.appendChild(editBtn);
}

function handleFavoritesClick(e: MouseEvent): void {
  processInteraction(e.target as HTMLElement);
}

function handleFavoritesTouch(e: TouchEvent): void {
  e.preventDefault();
  processInteraction(e.target as HTMLElement);
}

function processInteraction(target: HTMLElement): void {
  const removeKey = target.dataset.removeKey;
  if (removeKey) {
    removeFavorite(removeKey);
    return;
  }

  const button = target.closest<HTMLButtonElement>('.touch-key');
  if (!button) return;

  const action = button.dataset.action;
  if (action === 'edit-favorites' || action === 'save-favorites') {
    toggleEditMode();
    return;
  }

  if (isEditMode) return;

  const key = button.dataset.key;
  if (key) {
    sendFavoriteKey(key);
  }
}

function sendFavoriteKey(key: string): void {
  const sessionId = $activeSessionId.get();
  if (!sessionId) return;

  const mods = consumeModifiers();
  let sequence = KEY_SEQUENCES[key];

  if (!sequence) return;

  if (mods.ctrl || mods.alt || mods.shift) {
    if (sequence.length === 1 && mods.ctrl) {
      const charCode = sequence.charCodeAt(0);
      if (charCode >= 65 && charCode <= 90) {
        sequence = String.fromCharCode(charCode - 64);
      } else if (charCode >= 97 && charCode <= 122) {
        sequence = String.fromCharCode(charCode - 96);
      }
    }
  }

  sendInput(sessionId, sequence);
}
