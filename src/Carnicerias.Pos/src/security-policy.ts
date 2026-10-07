import type { WebPreferences } from 'electron';

const developmentOrigin = 'http://localhost:4200';

export function createSecureWebPreferences(preload: string): WebPreferences {
  return {
    contextIsolation: true,
    nodeIntegration: false,
    preload,
    sandbox: true,
    webSecurity: true,
  };
}

export function isAllowedNavigation(candidate: string): boolean {
  try {
    const url = new URL(candidate);
    return (
      url.origin === developmentOrigin ||
      (url.protocol === 'app:' && url.hostname === 'bundle')
    );
  } catch {
    return false;
  }
}
