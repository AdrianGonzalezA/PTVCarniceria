import { routes } from './app.routes';

describe('SuperAdmin route', () => {
  it('uses the SuperAdmin access path instead of the old manufacturer path', () => {
    expect(routes.some((route) => route.path === 'superadmin')).toBe(true);
    expect(routes.some((route) => route.path === 'fabricante')).toBe(false);
  });
});
