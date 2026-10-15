import { parseBranchConnectionBundle } from './central-branch-connection-bundle';

describe('parseBranchConnectionBundle', () => {
  const valid = {
    version: 1,
    label: 'Alta Córdoba',
    destinationUrl: 'http://127.0.0.1:5249/',
    installationId: '11111111-1111-4111-8111-111111111111',
    remoteCompanyId: '22222222-2222-4222-8222-222222222222',
    remoteBranchId: '33333333-3333-4333-8333-333333333333',
    token: 'A'.repeat(64),
  };

  it('accepts the exact versioned package exported by a branch', () => {
    expect(parseBranchConnectionBundle(JSON.stringify(valid))).toEqual(valid);
  });

  it('rejects malformed or additional fields before filling the central form', () => {
    expect(() => parseBranchConnectionBundle('{not json}')).toThrow();
    expect(() => parseBranchConnectionBundle(JSON.stringify({ ...valid, version: 2 }))).toThrow();
    expect(() => parseBranchConnectionBundle(JSON.stringify({ ...valid, token: 'short' }))).toThrow();
    expect(() => parseBranchConnectionBundle(JSON.stringify({ ...valid, extra: 'ignored' }))).toThrow();
  });
});
