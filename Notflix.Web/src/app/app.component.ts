import { AsyncPipe } from '@angular/common';
import { Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { BehaviorSubject, finalize } from 'rxjs';
import { AuthService, requestError } from './services/auth.service';
@Component({ selector: 'app-root', standalone: true, imports: [RouterOutlet, RouterLink, AsyncPipe],
  templateUrl: './app.component.html', styleUrls: ['./app.component.scss'] })
export class AppComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly error$ = new BehaviorSubject('');
  readonly loggingOut$ = new BehaviorSubject(false);
  logout(): void {
    if (this.loggingOut$.value) return;
    this.loggingOut$.next(true); this.error$.next('');
    this.auth.logout().pipe(finalize(() => this.loggingOut$.next(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => void this.router.navigate(['/']),
      error: error => { if (error.status === 401) { this.auth.clearSession(); void this.router.navigate(['/']); }
        else this.error$.next(requestError(error, 'Could not log out. Please try again.')); }
    });
  }
}
