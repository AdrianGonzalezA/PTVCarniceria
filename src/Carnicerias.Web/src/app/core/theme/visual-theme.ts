export const visualThemes = [
  { id: 'original', label: 'Original' },
  { id: 'nocturno', label: 'Nocturno' },
] as const;

export type VisualThemeId = typeof visualThemes[number]['id'];
export const defaultVisualThemeId: VisualThemeId = 'original';
export const visualThemeStorageKey = 'carnicerias.visual-theme.v1';

type VisualThemeStorage = Pick<Storage, 'getItem' | 'setItem'>;

function browserThemeStorage(): VisualThemeStorage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

function knownThemeId(value: string | null): VisualThemeId {
  return visualThemes.find((theme) => theme.id === value)?.id ?? defaultVisualThemeId;
}

export function initializeVisualTheme(
  root: HTMLElement = document.documentElement,
  storage: VisualThemeStorage | null = browserThemeStorage(),
): VisualThemeId {
  let themeId: VisualThemeId = defaultVisualThemeId;
  try {
    themeId = knownThemeId(storage?.getItem(visualThemeStorageKey) ?? null);
  } catch {
    // A disabled or inaccessible profile store must not prevent startup.
  }
  root.dataset['visualTheme'] = themeId;
  return themeId;
}

export function selectVisualTheme(
  themeId: VisualThemeId,
  root: HTMLElement = document.documentElement,
  storage: VisualThemeStorage | null = browserThemeStorage(),
): VisualThemeId {
  const selected = knownThemeId(themeId);
  try {
    if (!storage) throw new Error('Theme storage unavailable');
    storage.setItem(visualThemeStorageKey, selected);
    root.dataset['visualTheme'] = selected;
    return selected;
  } catch {
    root.dataset['visualTheme'] = defaultVisualThemeId;
    return defaultVisualThemeId;
  }
}
