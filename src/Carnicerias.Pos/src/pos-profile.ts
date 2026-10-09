import path from 'node:path';

export interface PosProfile {
  name: string | undefined;
  userDataPath: string;
}

export function resolvePosProfile(args: readonly string[], existingUserDataPath: string): PosProfile {
  const requested = args.filter((arg) => arg.startsWith('--pos-profile='));
  if (requested.length === 0) {
    return { name: undefined, userDataPath: existingUserDataPath };
  }

  const name = requested[0]?.slice('--pos-profile='.length) ?? '';
  if (requested.length !== 1 || !/^[a-z0-9][a-z0-9-]{0,39}$/u.test(name)) {
    throw new Error('Invalid POS profile');
  }

  return { name, userDataPath: path.join(existingUserDataPath, 'profiles', name) };
}

export function initialUrlForProfile(profile: PosProfile): string {
  return profile.name?.startsWith('caja-') ? 'app://bundle/pos' : 'app://bundle/index.html';
}
