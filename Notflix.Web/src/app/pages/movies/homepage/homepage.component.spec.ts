import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { Subject, of, throwError } from 'rxjs';
import { HomepageComponent } from './homepage.component';
import { MovieApiService } from '../../../services/movies/movie-api.service';
import { AuthService } from '../../../services/auth.service';

const movie = { id: 7, title: 'Arrival', category: 4, legacyRating: null, averageRating: null, ratingCount: 0, myRating: null };
describe('HomepageComponent', () => {
  const api = { getMovies: vi.fn(), rateMovie: vi.fn(), deleteMovie: vi.fn() };
  const auth = { clearSession: vi.fn() }; const router = { navigate: vi.fn() };
  beforeEach(() => {
    vi.clearAllMocks(); api.getMovies.mockReturnValue(of([movie]));
    TestBed.configureTestingModule({ imports: [HomepageComponent], providers: [{ provide: MovieApiService, useValue: api }, { provide: AuthService, useValue: auth }, { provide: Router, useValue: router }, { provide: MatDialog, useValue: { open: vi.fn() } }] });
  });
  it('renders loading, server catalogue, and a useful empty search state', () => {
    const pending = new Subject<typeof movie[]>(); api.getMovies.mockReturnValue(pending);
    const fixture = TestBed.createComponent(HomepageComponent); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('Loading movies');
    pending.next([movie]); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('Arrival'); expect(fixture.nativeElement.textContent).toContain('Not rated');
    fixture.componentInstance.search('no match'); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('No movies found');
  });
  it('retries a failed load and replaces the error with server results', () => {
    api.getMovies.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 503 }))).mockReturnValueOnce(of([movie]));
    const fixture = TestBed.createComponent(HomepageComponent); fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Could not load movies');
    fixture.componentInstance.reload(); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('Arrival'); expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
  it('saves a rating once, reloads server data and announces success', () => {
    const pending = new Subject<typeof movie>(); api.rateMovie.mockReturnValue(pending);
    const fixture = TestBed.createComponent(HomepageComponent); fixture.detectChanges();
    fixture.componentInstance.rate(movie, 5); fixture.componentInstance.rate(movie, 4); expect(api.rateMovie).toHaveBeenCalledOnce(); expect(api.rateMovie).toHaveBeenCalledWith(7, 5);
    pending.next(movie); pending.complete(); fixture.detectChanges(); expect(api.getMovies).toHaveBeenCalledTimes(2); expect(fixture.nativeElement.textContent).toContain('rating for Arrival was saved'); expect(fixture.componentInstance.pendingId$.value).toBeNull();
  });
  it('renders the server own-rating selection after the option controls are created', async () => {
    api.getMovies.mockReturnValue(of([{ ...movie, myRating: 5, averageRating: 5, ratingCount: 1 }]));
    const fixture = TestBed.createComponent(HomepageComponent); fixture.detectChanges();
    await fixture.whenStable(); fixture.detectChanges();
    const select: HTMLSelectElement = fixture.nativeElement.querySelector('select');
    expect(select.selectedOptions[0].textContent?.trim()).toBe('5 / 5');
  });
  it('reloads the server rating after a failed write and keeps its error actionable', async () => {
    api.getMovies.mockReturnValue(of([{ ...movie, myRating: 5, averageRating: 5, ratingCount: 1 }]));
    api.rateMovie.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 503 })));
    const fixture = TestBed.createComponent(HomepageComponent); fixture.detectChanges();
    fixture.componentInstance.rate(movie, 1); await fixture.whenStable(); fixture.detectChanges();
    expect(api.getMovies).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Rating was not saved');
    const select: HTMLSelectElement = fixture.nativeElement.querySelector('select');
    expect(select.selectedOptions[0].textContent?.trim()).toBe('5 / 5');
  });
  it('clears an expired session and sends the user back to login', () => {
    api.getMovies.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    const fixture = TestBed.createComponent(HomepageComponent); fixture.detectChanges();
    expect(auth.clearSession).toHaveBeenCalledOnce(); expect(router.navigate).toHaveBeenCalledWith(['/'], { queryParams: { expired: '1' } });
  });
});
