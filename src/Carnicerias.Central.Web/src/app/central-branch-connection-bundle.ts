import { CreateCentralBranchConnection } from './central-branch-connection-client';

export interface BranchConnectionBundle extends CreateCentralBranchConnection {
  readonly version: 1;
}

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const fields = ['version', 'label', 'destinationUrl', 'installationId',
  'remoteCompanyId', 'remoteBranchId', 'token'];

export function parseBranchConnectionBundle(raw: string): BranchConnectionBundle {
  if (raw.length > 5000) throw new Error('El paquete es demasiado grande.');
  const value: unknown = JSON.parse(raw);
  if (value === null || typeof value !== 'object' || Array.isArray(value) ||
      Object.keys(value).length !== fields.length ||
      fields.some(field => !Object.hasOwn(value, field)))
    throw new Error('El paquete no tiene los campos esperados.');

  const data = value as Record<string, unknown>;
  if (data['version'] !== 1 || typeof data['label'] !== 'string' ||
      data['label'].trim().length < 1 || data['label'].length > 200 ||
      typeof data['destinationUrl'] !== 'string' ||
      typeof data['token'] !== 'string' || !/^[0-9a-f]{64}$/i.test(data['token']) ||
      ![data['installationId'], data['remoteCompanyId'], data['remoteBranchId']]
        .every(id => typeof id === 'string' && guid.test(id)))
    throw new Error('El paquete contiene valores inválidos.');

  const url = new URL(data['destinationUrl']);
  if (url.username || url.password || url.pathname !== '/' || url.search || url.hash ||
      (url.protocol !== 'https:' &&
        !(url.protocol === 'http:' && url.hostname === '127.0.0.1')))
    throw new Error('La URL de sucursal no es válida.');
  return data as unknown as BranchConnectionBundle;
}
