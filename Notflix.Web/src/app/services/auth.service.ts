import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { BehaviorSubject, Observable, catchError, map, of, switchMap, tap, throwError } from 'rxjs';

export interface SessionUser { id: number; userName: string; }
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly userSubject = new BehaviorSubject<SessionUser | null>(null);
  readonly user$ = this.userSubject.asObservable();
  csrf(): Observable<void> { return this.http.get<void>('/api/auth/csrf'); }
  refreshSession(): Observable<SessionUser | null> {
    return this.http.get<SessionUser>('/api/auth/me').pipe(
      tap(user => this.userSubject.next(user)),
      catchError((error: HttpErrorResponse) => {
        if (error.status === 401) { this.userSubject.next(null); return of(null); }
        return throwError(() => error);
      })
    );
  }
  login(userName: string, password: string): Observable<SessionUser> {
    return this.csrf().pipe(
      switchMap(() => this.http.post<SessionUser>('/api/auth/login', { userName: userName.trim(), password })),
      tap(user => this.userSubject.next(user)),
      switchMap(user => this.csrf().pipe(map(() => user)))
    );
  }
  logout(): Observable<void> {
    return this.csrf().pipe(switchMap(() => this.http.post<void>('/api/auth/logout', {})),
      tap(() => this.userSubject.next(null)), switchMap(() => this.csrf()));
  }
  clearSession(): void { this.userSubject.next(null); }
}
export function requestError(error: HttpErrorResponse, fallback: string): string {
  if (error.status === 401) return 'Your session expired. Please log in again.';
  if (error.status === 429) return 'Too many requests. Please wait a minute and try again.';
  if (error.status === 0) return 'Cannot reach Notflix. Check the connection and try again.';
  return error.error?.message || error.error?.title || fallback;
}
