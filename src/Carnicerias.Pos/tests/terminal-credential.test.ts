import { mkdtemp, readFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { loadTerminalCredential } from '../src/terminal-credential';

describe('per-profile terminal credential', () => {
  let profilePath: string;
  const token = `${'a'.repeat(42)}b`;
  const protector = {
    isEncryptionAvailable: () => true,
    encryptStringAsync: async (value: string) => Buffer.from(value.split('').reverse().join('')),
    decryptStringAsync: async (value: Buffer) => ({
      result: value.toString().split('').reverse().join(''),
      shouldReEncrypt: false,
    }),
  };

  beforeEach(async () => {
    profilePath = await mkdtemp(path.join(os.tmpdir(), 'carnicerias-pos-'));
  });

  afterEach(async () => {
    await rm(profilePath, { recursive: true, force: true });
  });

  it('stores only protected bytes and recovers the same terminal on restart', async () => {
    expect(await loadTerminalCredential(profilePath, token, protector)).toBe(token);
    expect(await readFile(path.join(profilePath, 'terminal-credential.bin'), 'utf8')).not.toBe(token);
    expect(await loadTerminalCredential(profilePath, undefined, protector)).toBe(token);
  });

  it('does not silently rebind a profile to another register', async () => {
    await loadTerminalCredential(profilePath, token, protector);
    await expect(loadTerminalCredential(profilePath, 'b'.repeat(43), protector)).rejects.toThrow(
      'POS profile already belongs to another terminal',
    );
  });

  it('rejects unprotected storage and malformed credentials', async () => {
    await expect(loadTerminalCredential(profilePath, token, {
      ...protector,
      isEncryptionAvailable: () => false,
    })).rejects.toThrow('Secure credential storage unavailable');
    await expect(loadTerminalCredential(profilePath, 'invalid', protector)).rejects.toThrow(
      'Invalid terminal credential',
    );
    expect(await loadTerminalCredential(profilePath, undefined, protector)).toBeUndefined();
  });
});
