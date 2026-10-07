import { describe, expect, it } from 'vitest';
import { createSecureWebPreferences, isAllowedNavigation } from '../src/security-policy';

describe('Electron security policy', () => {
  it('keeps Node integration disabled with isolation and sandbox enabled', () => {
    expect(createSecureWebPreferences('C:\\app\\preload.js')).toMatchObject({
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    });
  });

  it('allows only the exact packaged and development origins', () => {
    expect(isAllowedNavigation('app://bundle/index.html')).toBe(true);
    expect(isAllowedNavigation('http://localhost:4200/login')).toBe(true);
    expect(isAllowedNavigation('http://localhost:4200.evil.test/')).toBe(false);
    expect(isAllowedNavigation('https://example.com/')).toBe(false);
  });
});
