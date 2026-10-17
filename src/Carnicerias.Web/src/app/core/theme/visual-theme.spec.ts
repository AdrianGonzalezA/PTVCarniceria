import {
  defaultVisualThemeId,
  initializeVisualTheme,
  selectVisualTheme,
  visualThemes,
  visualThemeStorageKey,
} from './visual-theme';

describe('visual theme preference', () => {
  const root = document.createElement('div');
  let savedTheme: string | null;
  let storage: Pick<Storage, 'getItem' | 'setItem'>;

  beforeEach(() => {
    root.removeAttribute('data-visual-theme');
    savedTheme = null;
    storage = {
      getItem: (key) => key === visualThemeStorageKey ? savedTheme : null,
      setItem: (key, value) => {
        if (key === visualThemeStorageKey) savedTheme = value;
      },
    };
  });

  it('registers stable IDs for Original and Nocturno', () => {
    expect(visualThemes).toEqual([
      { id: 'original', label: 'Original' },
      { id: 'nocturno', label: 'Nocturno' },
    ]);
  });

  it('applies Original before render when the profile has no preference', () => {
    expect(initializeVisualTheme(root, storage)).toBe(defaultVisualThemeId);
    expect(root.dataset['visualTheme']).toBe('original');
  });

  it('restores a saved preference and ignores unknown IDs', () => {
    savedTheme = 'nocturno';
    expect(initializeVisualTheme(root, storage)).toBe('nocturno');
    expect(root.dataset['visualTheme']).toBe('nocturno');

    savedTheme = 'unexpected-theme';
    expect(initializeVisualTheme(root, storage)).toBe('original');
    expect(root.dataset['visualTheme']).toBe('original');
  });

  it('falls back to Original when local storage cannot be read', () => {
    const unavailable = { ...storage, getItem: () => { throw new Error('storage denied'); } };
    expect(initializeVisualTheme(root, unavailable)).toBe('original');
    expect(initializeVisualTheme(root, null)).toBe('original');
    expect(root.dataset['visualTheme']).toBe('original');
  });

  it('persists a selection and applies it immediately', () => {
    expect(selectVisualTheme('nocturno', root, storage)).toBe('nocturno');
    expect(savedTheme).toBe('nocturno');
    expect(root.dataset['visualTheme']).toBe('nocturno');
    expect(initializeVisualTheme(root, storage)).toBe('nocturno');
  });

  it('uses Original if a new selection cannot be persisted', () => {
    const unavailable = { ...storage, setItem: () => { throw new Error('storage denied'); } };
    expect(selectVisualTheme('nocturno', root, unavailable)).toBe('original');
    expect(selectVisualTheme('nocturno', root, null)).toBe('original');
    expect(root.dataset['visualTheme']).toBe('original');
  });
});
