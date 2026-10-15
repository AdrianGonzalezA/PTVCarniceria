import { provideHttpClient } from '@angular/common/http';
import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';

// Angular HttpClient setup: https://angular.dev/guide/http/setup
bootstrapApplication(App, { providers: [provideHttpClient()] }).catch((error) => {
  console.error('No se pudo iniciar la administración central.', error);
});
