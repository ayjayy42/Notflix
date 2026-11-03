import { AsyncPipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { BehaviorSubject, catchError, combineLatest, finalize, map, of, shareReplay, startWith, switchMap } from 'rxjs';
import { Movie, categoryName } from '../../../models/movies';
import { MovieApiService } from '../../../services/movies/movie-api.service';
import { AuthService, requestError } from '../../../services/auth.service';
import { MovieDialogComponent } from '../movie-dialog/movie-dialog.component';
interface MovieState { movies: Movie[]; loading: boolean; error: string; }
@Component({ selector: 'app-home', standalone: true, imports: [AsyncPipe, DecimalPipe, FormsModule, MatDialogModule],
  templateUrl: './homepage.component.html', styleUrls: ['./homepage.component.scss'] })
export class HomepageComponent {
  private readonly api = inject(MovieApiService);
  private readonly dialog = inject(MatDialog);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload$ = new BehaviorSubject<void>(undefined);
  private readonly search$ = new BehaviorSubject('');
  readonly pendingId$ = new BehaviorSubject<number | null>(null);
  readonly actionError$ = new BehaviorSubject('');
  readonly notice$ = new BehaviorSubject('');
  readonly categoryName = categoryName;
  readonly stars = [1, 2, 3, 4, 5];
  private readonly loadState$ = this.reload$.pipe(switchMap(() => this.api.getMovies().pipe(
    map(movies => ({ movies, loading: false, error: '' } as MovieState)),
    startWith({ movies: [], loading: true, error: '' } as MovieState),
    catchError(error => { if (error.status === 401) this.expired();
      return of({ movies: [], loading: false, error: requestError(error, 'Could not load movies. Please retry.') } as MovieState); })
  )));
  readonly state$ = combineLatest([this.loadState$, this.search$]).pipe(
    map(([state, search]) => ({ ...state, movies: state.movies.filter(m => m.title.toLowerCase().includes(search.trim().toLowerCase())) })),
    shareReplay({ bufferSize: 1, refCount: true })
  );
  search(value: string): void { this.search$.next(value); }
  reload(): void { this.reload$.next(); }
  private expired(): void { this.auth.clearSession(); void this.router.navigate(['/'], { queryParams: { expired: '1' } }); }
  openMovieDialog(movie?: Movie): void {
    this.dialog.open(MovieDialogComponent, { width: '480px', maxWidth: '95vw', data: { movie }, autoFocus: 'first-tabbable', restoreFocus: true })
      .afterClosed().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(saved => {
        if (saved) { this.notice$.next(movie ? 'Movie updated.' : 'Movie added.'); this.actionError$.next(''); this.reload(); }
      });
  }
  rate(movie: Movie, rating: number): void {
    if (this.pendingId$.value !== null || rating < 1 || rating > 5) return;
    this.pendingId$.next(movie.id); this.actionError$.next(''); this.notice$.next('');
    this.api.rateMovie(movie.id, rating).pipe(finalize(() => this.pendingId$.next(null)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.notice$.next(`Your rating for ${movie.title} was saved.`); this.reload(); },
      error: error => { if (error.status === 401) this.expired(); else { this.actionError$.next(requestError(error, 'Rating was not saved. Please retry.')); this.reload(); } }
    });
  }
  delete(movie: Movie): void {
    if (this.pendingId$.value !== null || !window.confirm(`Delete "${movie.title}" and its ratings?`)) return;
    this.pendingId$.next(movie.id); this.actionError$.next('');
    this.api.deleteMovie(movie.id).pipe(finalize(() => this.pendingId$.next(null)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.notice$.next('Movie deleted.'); this.reload(); },
      error: error => { if (error.status === 401) this.expired(); else this.actionError$.next(requestError(error, 'Movie was not deleted.')); }
    });
  }
}
