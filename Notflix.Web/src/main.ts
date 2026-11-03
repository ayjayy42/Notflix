import { bootstrapApplication } from '@angular/platform-browser';
import { provideZoneChangeDetection } from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { AppComponent } from './app/app.component';
import { appRoutes } from './app/app.routes';
bootstrapApplication(AppComponent, { providers: [provideRouter(appRoutes), provideHttpClient(),
  provideZoneChangeDetection({ eventCoalescing: true }), provideAnimationsAsync()] }).catch(console.error);
