import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { resolveAppAsset, toBackendUrl } from '../src/app-protocol';

describe('app protocol boundary', () => {
  it('resolves packaged assets only below the staged web root', () => {
    const root = path.resolve('staged-web');

    expect(resolveAppAsset(root, 'app://bundle/main.js')).toBe(path.join(root, 'main.js'));
    expect(() => resolveAppAsset(root, 'app://bundle/%2e%2e/secret.txt')).toThrow(
      'Invalid application asset path',
    );
  });

  it('serves the Angular entry point when a POS route is reloaded', () => {
    const root = path.resolve('staged-web');

    expect(resolveAppAsset(root, 'app://bundle/pos')).toBe(path.join(root, 'index.html'));
    expect(resolveAppAsset(root, 'app://bundle/users')).toBe(path.join(root, 'index.html'));
    expect(resolveAppAsset(root, 'app://bundle/pos?ticket=A')).toBe(path.join(root, 'index.html'));
  });

  it('maps only app API requests to the fixed local backend', () => {
    expect(toBackendUrl('app://bundle/api/health')).toBe('http://localhost:5197/api/health');
    expect(toBackendUrl('app://bundle/assets/logo.svg')).toBeUndefined();
  });
});
