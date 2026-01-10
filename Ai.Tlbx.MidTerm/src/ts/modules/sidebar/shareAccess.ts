/**
 * Share Access Module
 *
 * Handles the "Share Access" button that opens email client
 * with connection info for sharing terminal access with others.
 */

import { createLogger } from '../logging';

const log = createLogger('shareAccess');

interface NetworkEndpointInfo {
  name: string;
  url: string;
}

interface CertificateDownloadInfo {
  fingerprint: string;
  fingerprintFormatted: string;
  notBefore: string;
  notAfter: string;
  keyProtection: string;
  dnsNames: string[];
  ipAddresses: string[];
  isFallbackCertificate: boolean;
}

interface SharePacketInfo {
  certificate: CertificateDownloadInfo;
  endpoints: NetworkEndpointInfo[];
  trustPageUrl: string;
  port: number;
}

export function initShareAccessButton(): void {
  const el = document.getElementById('btn-share-access');
  console.log('[shareAccess] initShareAccessButton: element found =', !!el);
  if (el) {
    el.addEventListener('click', () => {
      console.log('[shareAccess] Share Access button clicked');
      openShareEmail();
    });
  }
}

async function openShareEmail(): Promise<void> {
  try {
    const response = await fetch('/api/certificate/share-packet');
    if (!response.ok) {
      log.error(() => 'Failed to fetch share packet');
      return;
    }

    const info: SharePacketInfo = await response.json();
    const subject = `MidTerm Terminal Access - ${location.hostname}`;
    const body = generateEmailBody(info);

    window.location.href = `mailto:?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
  } catch (e) {
    log.error(() => `Failed to open share email: ${e}`);
  }
}

function generateEmailBody(info: SharePacketInfo): string {
  const endpointsList = info.endpoints
    .map(ep => `• ${ep.name}: ${ep.url}`)
    .join('\n');

  const validUntil = new Date(info.certificate.notAfter).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'long',
    day: 'numeric'
  });

  return `MidTerm Terminal Access
=======================

SECURITY: VERIFY FINGERPRINT FIRST
----------------------------------
SHA-256: ${info.certificate.fingerprintFormatted}

Compare this with your browser's certificate fingerprint before entering any passwords.
Click the padlock icon in your browser's address bar > Certificate > SHA-256 fingerprint.

CONNECTION ENDPOINTS
--------------------
${endpointsList}

INSTALL CERTIFICATE
-------------------
Visit: ${info.trustPageUrl}

This page will detect your device and guide you through installation.

Certificate valid until: ${validUntil}

TIP: Send this email to yourself, your work email, and family members
who may need terminal access from their phones or tablets.

---
MidTerm - Web Terminal Multiplexer
`;
}
