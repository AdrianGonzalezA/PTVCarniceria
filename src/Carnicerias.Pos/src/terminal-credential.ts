import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

interface CredentialProtector {
  isEncryptionAvailable(): boolean;
  encryptStringAsync(value: string): Promise<Buffer>;
  decryptStringAsync(value: Buffer): Promise<{ result: string; shouldReEncrypt: boolean }>;
}

const credentialFileName = 'terminal-credential.bin';

function validateCredential(value: string): void {
  if (!/^[A-Za-z0-9_-]{43}$/u.test(value)) {
    throw new Error('Invalid terminal credential');
  }
}

export async function loadTerminalCredential(
  userDataPath: string,
  initialCredential: string | undefined,
  protector: CredentialProtector,
): Promise<string | undefined> {
  if (initialCredential !== undefined) validateCredential(initialCredential);

  const filePath = path.join(userDataPath, credentialFileName);
  let encrypted: Buffer | undefined;
  try {
    encrypted = await readFile(filePath);
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
  }

  if (!encrypted && !initialCredential) return undefined;
  if (!protector.isEncryptionAvailable()) {
    throw new Error('Secure credential storage unavailable');
  }

  if (encrypted) {
    const decrypted = await protector.decryptStringAsync(encrypted);
    validateCredential(decrypted.result);
    if (initialCredential && initialCredential !== decrypted.result) {
      throw new Error('POS profile already belongs to another terminal');
    }
    return decrypted.result;
  }

  const protectedBytes = await protector.encryptStringAsync(initialCredential!);
  await writeFile(filePath, protectedBytes, { flag: 'wx', mode: 0o600 });
  return initialCredential;
}
