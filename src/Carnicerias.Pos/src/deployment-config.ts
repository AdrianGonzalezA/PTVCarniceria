import path from 'node:path';
import { isIP } from 'node:net';

export interface SerialDeviceConfig {
  readonly port: string;
  readonly baudRate: number;
}

export interface DeploymentConfig {
  readonly apiOrigin: string;
  readonly scale: SerialDeviceConfig;
  readonly printer: SerialDeviceConfig;
}

const developmentConfig: DeploymentConfig = {
  apiOrigin: 'http://localhost:5197',
  scale: { port: 'COM6', baudRate: 9600 },
  printer: { port: 'COM1', baudRate: 9600 },
};
const baudRates = new Set([1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200]);

function record(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new Error('POS_CONFIG_INVALID');
  return value as Record<string, unknown>;
}

function privateIpv4(host: string): boolean {
  if (isIP(host) !== 4) return false;
  const [a, b = -1] = host.split('.').map(Number);
  return a === 10 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168);
}

function apiOrigin(value: unknown, allowInsecureLan: unknown): string {
  if (typeof value !== 'string') throw new Error('POS_API_ORIGIN_INVALID');
  let url: URL;
  try { url = new URL(value); }
  catch { throw new Error('POS_API_ORIGIN_INVALID'); }
  if (url.username || url.password || url.pathname !== '/' || url.search || url.hash ||
      (url.protocol !== 'http:' && url.protocol !== 'https:'))
    throw new Error('POS_API_ORIGIN_INVALID');
  if (url.protocol === 'http:' && url.hostname !== 'localhost' &&
      url.hostname !== '127.0.0.1' && !(allowInsecureLan === true && privateIpv4(url.hostname)))
    throw new Error('POS_API_ORIGIN_REQUIRES_HTTPS');
  return url.origin;
}

function serialDevice(value: unknown): SerialDeviceConfig {
  const input = record(value);
  if (typeof input.port !== 'string' || !/^COM[1-9][0-9]{0,2}$/iu.test(input.port) ||
      typeof input.baudRate !== 'number' || !baudRates.has(input.baudRate))
    throw new Error('POS_SERIAL_CONFIG_INVALID');
  return { port: input.port.toUpperCase(), baudRate: input.baudRate };
}

export function parseDeploymentConfig(value: unknown, packaged: boolean): DeploymentConfig {
  if (value === undefined) {
    if (packaged) throw new Error('POS_CONFIG_REQUIRED');
    return developmentConfig;
  }
  const input = record(value);
  return {
    apiOrigin: apiOrigin(input.apiOrigin, input.allowInsecureLan),
    scale: serialDevice(input.scale),
    printer: serialDevice(input.printer),
  };
}

export function resolveConfigPath(args: readonly string[]): string | undefined {
  const paths = args.filter((arg) => arg.startsWith('--pos-config='));
  if (paths.length === 0) return undefined;
  const selected = paths[0]?.slice('--pos-config='.length) ?? '';
  if (paths.length !== 1 || !path.isAbsolute(selected)) throw new Error('POS_CONFIG_PATH_INVALID');
  return selected;
}
