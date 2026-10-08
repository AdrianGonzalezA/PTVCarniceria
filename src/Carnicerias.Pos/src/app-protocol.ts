import path from 'node:path';

const applicationOrigin = 'app://bundle';
const backendOrigin = 'http://localhost:5197';
const angularRoutes = new Set(['', '/', '/pos', '/users', '/admin', '/admin/categories', '/admin/products', '/admin/price-lists']);

export function resolveAppAsset(webRoot: string, candidate: string): string {
  const url = new URL(candidate);
  const rawPath = candidate.slice(applicationOrigin.length).split(/[?#]/u, 1)[0] ?? '';
  const decodedPath = decodeURIComponent(rawPath);

  if (
    url.protocol !== 'app:' ||
    url.hostname !== 'bundle' ||
    decodedPath.split('/').includes('..')
  ) {
    throw new Error('Invalid application asset path');
  }

  const relativePath = angularRoutes.has(decodedPath) ? 'index.html' : decodedPath.slice(1);
  const resolvedRoot = path.resolve(webRoot);
  const resolvedAsset = path.resolve(resolvedRoot, relativePath);
  const relative = path.relative(resolvedRoot, resolvedAsset);

  if (relative.startsWith('..') || path.isAbsolute(relative)) {
    throw new Error('Invalid application asset path');
  }

  return resolvedAsset;
}

export function toBackendUrl(candidate: string): string | undefined {
  const url = new URL(candidate);
  if (url.protocol !== 'app:' || url.hostname !== 'bundle' || !url.pathname.startsWith('/api/')) {
    return undefined;
  }

  return new URL(`${url.pathname}${url.search}`, backendOrigin).toString();
}

export function toBackendRequest(request: Request, terminalCredential?: string): Request {
  const backendUrl = toBackendUrl(request.url);
  if (!backendUrl) throw new Error('Invalid backend request');

  const forwarded = new Request(backendUrl, request);
  forwarded.headers.delete('X-Pos-Terminal-Credential');
  if (terminalCredential) {
    forwarded.headers.set('X-Pos-Terminal-Credential', terminalCredential);
  }
  return forwarded;
}
