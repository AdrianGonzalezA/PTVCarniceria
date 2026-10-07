import type { NativeDiagnostic } from './native-api';

export function createDiagnostic(
  platform: NodeJS.Platform,
  electronVersion: string,
): NativeDiagnostic {
  return {
    electronVersion,
    platform,
    status: 'ready',
  };
}
