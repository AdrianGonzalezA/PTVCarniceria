import { InjectionToken } from '@angular/core';

export const POS_ELECTRON_MODE = new InjectionToken<boolean>('POS Electron mode', {
  providedIn: 'root',
  factory: () => window.location.protocol === 'app:',
});
