import { describe, expect, it } from 'vitest';
import { parseDeploymentConfig, resolveConfigPath } from '../src/deployment-config';

const labConfig = {
  apiOrigin: 'http://192.168.10.20:5197',
  allowInsecureLan: true,
  scale: { port: 'COM7', baudRate: 4800 },
  printer: { port: 'COM3', baudRate: 9600 },
};

describe('POS deployment configuration', () => {
  it('requires an explicit configuration for a packaged terminal', () => {
    expect(() => parseDeploymentConfig(undefined, true)).toThrow('POS_CONFIG_REQUIRED');
  });

  it('keeps the current loopback devices only for development without a config file', () => {
    expect(parseDeploymentConfig(undefined, false)).toEqual({
      apiOrigin: 'http://localhost:5197',
      scale: { port: 'COM6', baudRate: 9600 },
      printer: { port: 'COM1', baudRate: 9600 },
    });
  });

  it('accepts an opted-in private LAN origin and per-terminal serial ports', () => {
    expect(parseDeploymentConfig(labConfig, true)).toEqual({
      apiOrigin: 'http://192.168.10.20:5197',
      scale: { port: 'COM7', baudRate: 4800 },
      printer: { port: 'COM3', baudRate: 9600 },
    });
  });

  it('rejects remote plaintext, credentials, paths and non-HTTP schemes', () => {
    for (const apiOrigin of [
      'http://192.168.10.20:5197',
      'http://example.com:5197',
      'https://user:pass@example.com',
      'https://example.com/other',
      'file:///C:/server',
    ]) {
      expect(() => parseDeploymentConfig({ ...labConfig, apiOrigin,
        allowInsecureLan: false }, true)).toThrow();
    }
    expect(() => parseDeploymentConfig({ ...labConfig,
      apiOrigin: 'http://example.com:5197' }, true)).toThrow();
  });

  it('rejects invalid serial port and baud settings', () => {
    expect(() => parseDeploymentConfig({ ...labConfig,
      scale: { port: 'COM0', baudRate: 9600 } }, true)).toThrow();
    expect(() => parseDeploymentConfig({ ...labConfig,
      printer: { port: 'COM3', baudRate: 12345 } }, true)).toThrow();
  });

  it('requires one absolute config path and does not infer a path from arbitrary arguments', () => {
    expect(resolveConfigPath(['app.exe', '--pos-config=C:\\POS\\caja.json']))
      .toBe('C:\\POS\\caja.json');
    expect(resolveConfigPath(['app.exe', '--pos-profile=caja-1'])).toBeUndefined();
    expect(() => resolveConfigPath(['--pos-config=relative.json'])).toThrow();
    expect(() => resolveConfigPath(['--pos-config=C:\\a.json', '--pos-config=C:\\b.json']))
      .toThrow();
  });
});
