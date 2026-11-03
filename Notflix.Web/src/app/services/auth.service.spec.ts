import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService, requestError } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;
  const user = { id: 7, userName: 'tester' };
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('obtains anonymous CSRF, logs in with relative URL, then refreshes the identity-bound CSRF token', () => {
    const received = vi.fn();
    let current: unknown;
    service.user$.subscribe(value => current = value);
    service.login('  tester  ', 'password').subscribe(received);
    http.expectOne('/api/auth/csrf').flush(null);
    const login = http.expectOne('/api/auth/login');
    expect(login.request.method).toBe('POST');
    expect(login.request.body).toEqual({ userName: 'tester', password: 'password' });
    login.flush(user);
    expect(current).toEqual(user);
    expect(received).not.toHaveBeenCalled();
    http.expectOne('/api/auth/csrf').flush(null);
    expect(received).toHaveBeenCalledWith(user);
  });

  it('keeps failed credentials out of session state and propagates login failure', () => {
    const failed = vi.fn();
    let current: unknown;
    service.user$.subscribe(value => current = value);
    service.login('tester', 'wrong').subscribe({ error: failed });
    http.expectOne('/api/auth/csrf').flush(null);
    http.expectOne('/api/auth/login').flush({ message: 'Invalid username or password.' }, { status: 401, statusText: 'Unauthorized' });
    expect(failed).toHaveBeenCalledOnce();
    expect(current).toBeNull();
    http.expectNone('/api/auth/csrf');
  });

  it('restores a session from the server', () => {
    const received = vi.fn();
    service.refreshSession().subscribe(received);
    http.expectOne('/api/auth/me').flush(user);
    expect(received).toHaveBeenCalledWith(user);
  });

  it('treats only an unauthorized session check as logged out', () => {
    const received = vi.fn();
    service.refreshSession().subscribe(received);
    http.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(received).toHaveBeenCalledWith(null);
  });

  it('propagates a session-check network failure so the user can retry', () => {
    const failed = vi.fn();
    service.refreshSession().subscribe({ error: failed });
    http.expectOne('/api/auth/me').error(new ProgressEvent('error'));
    expect(failed).toHaveBeenCalledOnce();
    expect(failed.mock.calls[0][0].status).toBe(0);
  });

  it('refreshes CSRF around logout and clears local session state', () => {
    const completed = vi.fn();
    service.logout().subscribe(completed);
    http.expectOne('/api/auth/csrf').flush(null);
    const logout = http.expectOne('/api/auth/logout');
    expect(logout.request.method).toBe('POST');
    logout.flush(null);
    expect(completed).not.toHaveBeenCalled();
    http.expectOne('/api/auth/csrf').flush(null);
    expect(completed).toHaveBeenCalledOnce();
  });

  it('maps network, expiration, throttling and server failures to actionable messages', () => {
    expect(requestError(new HttpErrorResponse({ status: 0 }), 'Fallback')).toContain('Cannot reach');
    expect(requestError(new HttpErrorResponse({ status: 401 }), 'Fallback')).toContain('session expired');
    expect(requestError(new HttpErrorResponse({ status: 429 }), 'Fallback')).toContain('wait a minute');
    expect(requestError(new HttpErrorResponse({ status: 500, error: { title: 'Server failure' } }), 'Fallback')).toBe('Server failure');
    expect(requestError(new HttpErrorResponse({ status: 500 }), 'Fallback')).toBe('Fallback');
  });
});
