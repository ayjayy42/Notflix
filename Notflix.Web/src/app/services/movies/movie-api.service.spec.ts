import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MovieApiService } from './movie-api.service';

const movie = { id: 7, title: 'Arrival', category: 4, legacyRating: null, averageRating: 4, ratingCount: 2, myRating: 5 };
describe('MovieApiService', () => {
  let service: MovieApiService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(MovieApiService); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('loads server catalogue and actual vote summaries from a relative API URL', () => {
    const received = vi.fn(); service.getMovies().subscribe(received);
    const request = http.expectOne('/api/movies'); expect(request.request.method).toBe('GET');
    request.flush([movie]); expect(received).toHaveBeenCalledWith([movie]);
  });
  it('creates and updates with the movie contract, targeting edits by route ID', () => {
    const body = { title: 'Arrival', category: 4 };
    service.addMovie(body).subscribe();
    const add = http.expectOne('/api/movies'); expect(add.request.method).toBe('POST'); expect(add.request.body).toEqual(body); add.flush(movie);
    service.updateMovie(7, body).subscribe();
    const edit = http.expectOne('/api/movies/7'); expect(edit.request.method).toBe('PUT'); expect(edit.request.body).toEqual(body); edit.flush(movie);
  });
  it('rates without a client-controlled user ID and supports deletion', () => {
    service.rateMovie(7, 5).subscribe();
    const rate = http.expectOne('/api/movies/7/rating'); expect(rate.request.method).toBe('PUT'); expect(rate.request.body).toEqual({ rating: 5 }); rate.flush(movie);
    service.deleteMovie(7).subscribe();
    const remove = http.expectOne('/api/movies/7'); expect(remove.request.method).toBe('DELETE'); remove.flush(null);
  });
  it('propagates write errors without automatically replaying the mutation', () => {
    const failed = vi.fn(); service.addMovie({ title: 'Arrival', category: 4 }).subscribe({ error: failed });
    http.expectOne('/api/movies').flush({}, { status: 500, statusText: 'Server Error' });
    expect(failed).toHaveBeenCalledOnce(); http.expectNone('/api/movies');
  });
});
