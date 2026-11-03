import { Routes } from '@angular/router';
import { authGuard } from './auth.guard';
export const appRoutes: Routes = [
  { path: '', loadComponent: () => import('./pages/loginpage/loginpage.component').then(m => m.LoginpageComponent) },
  { path: 'home', canActivate: [authGuard], loadComponent: () => import('./pages/movies/homepage/homepage.component').then(m => m.HomepageComponent) },
  { path: '**', redirectTo: '' }
];
