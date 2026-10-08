import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { resolveAppAsset, toBackendRequest, toBackendUrl } from '../src/app-protocol';

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
    expect(resolveAppAsset(root, 'app://bundle/admin')).toBe(path.join(root, 'index.html'));
    expect(resolveAppAsset(root, 'app://bundle/admin/categories')).toBe(path.join(root, 'index.html'));
    expect(resolveAppAsset(root, 'app://bundle/admin/products')).toBe(path.join(root, 'index.html'));
    expect(resolveAppAsset(root, 'app://bundle/admin/price-lists')).toBe(path.join(root, 'index.html'));
    expect(resolveAppAsset(root, 'app://bundle/admin/organization')).toBe(path.join(root, 'index.html'));
    expect(resolveAppAsset(root, 'app://bundle/admin/stock')).toBe(path.join(root, 'index.html'));
    expect(resolveAppAsset(root, 'app://bundle/admin/barcode-layouts')).toBe(path.join(root, 'index.html'));
    expect(resolveAppAsset(root, 'app://bundle/admin/history')).toBe(path.join(root, 'index.html'));
  });

  it('maps only app API requests to the fixed local backend', () => {
    expect(toBackendUrl('app://bundle/api/health')).toBe('http://localhost:5197/api/health');
    expect(toBackendUrl('app://bundle/assets/logo.svg')).toBeUndefined();
  });

  it('replaces a renderer-supplied terminal credential with the main-process credential', () => {
    const incoming = new Request('app://bundle/api/pos-terminals/current', {
      headers: { 'X-Pos-Terminal-Credential': 'forged-by-renderer' },
    });

    const authenticated = toBackendRequest(incoming, 'main-process-secret');
    expect(authenticated.url).toBe('http://localhost:5197/api/pos-terminals/current');
    expect(authenticated.headers.get('X-Pos-Terminal-Credential')).toBe('main-process-secret');

    const unprovisioned = toBackendRequest(incoming);
    expect(unprovisioned.headers.has('X-Pos-Terminal-Credential')).toBe(false);
  });

  it('preserves a sale request body while adding the terminal credential', async () => {
    const incoming = new Request('app://bundle/api/sale-drafts', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '{"priceListId":"test"}',
    });

    const forwarded = toBackendRequest(incoming, 'main-process-secret');
    expect(forwarded.method).toBe('POST');
    expect(await forwarded.text()).toBe('{"priceListId":"test"}');
    expect(forwarded.headers.get('Content-Type')).toBe('application/json');
    expect(forwarded.headers.get('X-Pos-Terminal-Credential')).toBe('main-process-secret');
  });
});
