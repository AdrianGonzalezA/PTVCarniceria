import { createRequire } from 'node:module';
import { describe, expect, it } from 'vitest';

const require = createRequire(import.meta.url);
const config = require('../forge.config.cjs') as {
  packagerConfig: { ignore: RegExp[] };
};

describe('Electron packaging', () => {
  it('excludes previous Forge output from the packaged application', () => {
    expect(config.packagerConfig.ignore.some((pattern) => pattern.test('/out/old/resources/app.asar')))
      .toBe(true);
  });
});
