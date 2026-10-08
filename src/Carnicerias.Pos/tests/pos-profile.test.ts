import path from 'node:path';
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { resolvePosProfile } from '../src/pos-profile';

describe('POS profile selection', () => {
  const originalUserData = path.resolve('user-data');

  it('keeps the existing Electron profile when no profile is requested', () => {
    expect(resolvePosProfile([], originalUserData)).toEqual({
      name: undefined,
      userDataPath: originalUserData,
    });
  });

  it('assigns a separate persistent data path to each named register', () => {
    const first = resolvePosProfile(['--pos-profile=caja-1'], originalUserData);
    const second = resolvePosProfile(['--pos-profile=caja-2'], originalUserData);

    expect(first.userDataPath).toBe(path.join(originalUserData, 'profiles', 'caja-1'));
    expect(second.userDataPath).toBe(path.join(originalUserData, 'profiles', 'caja-2'));
    expect(first.userDataPath).not.toBe(second.userDataPath);
  });

  it.each(['../other', 'Caja 1', 'caja/1', '', 'caja-1\\other'])('rejects unsafe names: %s', (name) => {
    expect(() => resolvePosProfile([`--pos-profile=${name}`], originalUserData)).toThrow(
      'Invalid POS profile',
    );
  });

  it('rejects duplicate profile arguments', () => {
    expect(() => resolvePosProfile([
      '--pos-profile=caja-1', '--pos-profile=caja-2',
    ], originalUserData)).toThrow('Invalid POS profile');
  });

  it('opens the provisioned first register by default in local development', () => {
    const packageJson = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8')) as {
      scripts: Record<string, string>;
    };

    expect(packageJson.scripts['electron:start']).toContain('electron . --pos-profile=caja-1');
    expect(packageJson.scripts['electron:start:caja2']).toContain('electron . --pos-profile=caja-2');
  });
});
