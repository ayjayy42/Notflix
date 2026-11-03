import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, Router, UrlTree, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { AuthService } from './services/auth.service';
import { authGuard } from './auth.guard';

describe('authGuard', () => {
  const auth = { refreshSession: vi.fn() };
  beforeEach(() => { vi.clearAllMocks(); TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthService, useValue: auth }] }); });
  function check(): Observable<boolean | UrlTree> {
    return TestBed.runInInjectionContext(() => authGuard({} as ActivatedRouteSnapshot, { url: '/home' } as RouterStateSnapshot)) as Observable<boolean | UrlTree>;
  }
  it('allows an authenticated server session', () => {
    auth.refreshSession.mockReturnValue(of({ id: 7, userName: 'tester' })); let result: unknown;
    check().subscribe(value => result = value); expect(result).toBe(true);
  });
  it('redirects an absent session to login with the destination', () => {
    auth.refreshSession.mockReturnValue(of(null)); let result: unknown;
    check().subscribe(value => result = value);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toContain('returnUrl=%2Fhome');
  });
  it('marks a failed session check as retryable rather than silently logged out', () => {
    auth.refreshSession.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 503 }))); let result: unknown;
    check().subscribe(value => result = value);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toContain('connectionError=1');
  });
});
