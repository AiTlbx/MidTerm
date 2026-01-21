/**
 * Touch Gestures Module
 *
 * Swipe and two-finger trackpad gestures for terminal control.
 * - Horizontal swipe: Ctrl+A (start) / Ctrl+E (end)
 * - Two-finger pan: Arrow key movement
 */

import { $activeSessionId } from '../../stores';
import { sendInput } from '../comms/muxChannel';

interface TouchStart {
  x: number;
  y: number;
  time: number;
}

interface TwoFingerStart {
  x: number;
  y: number;
}

const SWIPE_THRESHOLD = 80;
const SWIPE_MAX_VERTICAL = 40;
const SWIPE_MAX_TIME = 300;
const PAN_THRESHOLD = 25;

let singleTouchStart: TouchStart | null = null;
let twoFingerStart: TwoFingerStart | null = null;
let terminalsArea: HTMLElement | null = null;

export function initGestures(): void {
  terminalsArea = document.querySelector('.terminals-area');
  if (!terminalsArea) return;

  terminalsArea.addEventListener('touchstart', handleTouchStart, { passive: true });
  terminalsArea.addEventListener('touchmove', handleTouchMove, { passive: true });
  terminalsArea.addEventListener('touchend', handleTouchEnd, { passive: false });
}

export function teardownGestures(): void {
  if (terminalsArea) {
    terminalsArea.removeEventListener('touchstart', handleTouchStart);
    terminalsArea.removeEventListener('touchmove', handleTouchMove);
    terminalsArea.removeEventListener('touchend', handleTouchEnd);
  }
  terminalsArea = null;
  singleTouchStart = null;
  twoFingerStart = null;
}

function handleTouchStart(e: TouchEvent): void {
  if (isOnTouchController(e.target as HTMLElement)) return;

  const touch0 = e.touches[0];
  const touch1 = e.touches[1];

  if (e.touches.length === 1 && touch0) {
    singleTouchStart = {
      x: touch0.clientX,
      y: touch0.clientY,
      time: Date.now(),
    };
    twoFingerStart = null;
  } else if (e.touches.length === 2 && touch0 && touch1) {
    singleTouchStart = null;
    twoFingerStart = {
      x: (touch0.clientX + touch1.clientX) / 2,
      y: (touch0.clientY + touch1.clientY) / 2,
    };
  }
}

function handleTouchMove(e: TouchEvent): void {
  const touch0 = e.touches[0];
  const touch1 = e.touches[1];

  if (e.touches.length !== 2 || !twoFingerStart || !touch0 || !touch1) return;

  const sessionId = $activeSessionId.get();
  if (!sessionId) return;

  const cx = (touch0.clientX + touch1.clientX) / 2;
  const cy = (touch0.clientY + touch1.clientY) / 2;

  const dx = cx - twoFingerStart.x;
  const dy = cy - twoFingerStart.y;

  if (Math.abs(dx) >= PAN_THRESHOLD) {
    sendInput(sessionId, dx > 0 ? '\x1b[C' : '\x1b[D');
    twoFingerStart.x = cx;
  }

  if (Math.abs(dy) >= PAN_THRESHOLD) {
    sendInput(sessionId, dy > 0 ? '\x1b[B' : '\x1b[A');
    twoFingerStart.y = cy;
  }
}

function handleTouchEnd(e: TouchEvent): void {
  if (twoFingerStart && e.touches.length === 0) {
    twoFingerStart = null;
    return;
  }

  const touch = e.changedTouches[0];
  if (!singleTouchStart || e.changedTouches.length !== 1 || !touch) {
    singleTouchStart = null;
    return;
  }

  const dx = touch.clientX - singleTouchStart.x;
  const dy = touch.clientY - singleTouchStart.y;
  const dt = Date.now() - singleTouchStart.time;

  singleTouchStart = null;

  if (Math.abs(dx) < SWIPE_THRESHOLD || Math.abs(dy) > SWIPE_MAX_VERTICAL || dt > SWIPE_MAX_TIME) {
    return;
  }

  const sessionId = $activeSessionId.get();
  if (!sessionId) return;

  e.preventDefault();

  if (dx > 0) {
    sendInput(sessionId, '\x05');
  } else {
    sendInput(sessionId, '\x01');
  }
}

function isOnTouchController(target: HTMLElement): boolean {
  return !!target.closest('#touch-controller') || !!target.closest('.touch-popup');
}
