/**
 * Changelog Module
 *
 * Fetches and displays the changelog from GitHub releases.
 */

import { escapeHtml } from '../../utils';

const GITHUB_RELEASES_URL = 'https://api.github.com/repos/AiTlbx/MiddleManager/releases?per_page=10';
const GITHUB_RELEASES_PAGE = 'https://github.com/AiTlbx/MiddleManager/releases';

interface GitHubRelease {
  tag_name?: string;
  published_at?: string;
  body?: string;
}

/**
 * Show the changelog modal and fetch releases from GitHub
 */
export function showChangelog(): void {
  const modal = document.getElementById('changelog-modal');
  const body = document.getElementById('changelog-body');

  if (modal) modal.classList.remove('hidden');
  if (body) body.innerHTML = '<div class="changelog-loading">Loading changelog...</div>';

  fetch(GITHUB_RELEASES_URL)
    .then((r) => r.json())
    .then((releases: GitHubRelease[]) => {
      if (!body) return;

      if (!releases || releases.length === 0) {
        body.innerHTML = '<p>No releases found.</p>';
        return;
      }

      let html = '';
      releases.forEach((release) => {
        const version = release.tag_name || 'Unknown';
        const date = release.published_at
          ? new Date(release.published_at).toLocaleDateString()
          : '';
        const notes = release.body || 'No release notes.';

        html += '<div class="changelog-release">';
        html += '<div class="changelog-version">' + escapeHtml(version) + '</div>';
        if (date) html += '<div class="changelog-date">' + escapeHtml(date) + '</div>';
        html += '<div class="changelog-notes">' + formatMarkdown(notes) + '</div>';
        html += '</div>';
      });

      body.innerHTML = html;
    })
    .catch((e) => {
      if (body) {
        body.innerHTML =
          '<p class="changelog-error">Failed to load changelog. ' +
          '<a href="' + GITHUB_RELEASES_PAGE + '" target="_blank">View on GitHub</a></p>';
      }
      console.error('Changelog error:', e);
    });
}

/**
 * Close the changelog modal
 */
export function closeChangelog(): void {
  const modal = document.getElementById('changelog-modal');
  if (modal) modal.classList.add('hidden');
}

/**
 * Format markdown text to HTML (basic subset)
 *
 * Supports: headers (## and ###), bold (**text**),
 * links [text](url), and bullet lists (- item)
 */
export function formatMarkdown(text: string): string {
  return escapeHtml(text)
    .replace(/^### (.+)$/gm, '<h4>$1</h4>')
    .replace(/^## (.+)$/gm, '<h3>$1</h3>')
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" target="_blank">$1</a>')
    .replace(/^- (.+)$/gm, '<li>$1</li>')
    .replace(/(<li>.*<\/li>)/s, '<ul>$1</ul>')
    .replace(/\n/g, '<br>');
}
