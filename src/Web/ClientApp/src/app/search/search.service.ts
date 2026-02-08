import { Inject, Injectable, Optional } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import { API_BASE_URL, SearchResultsVm } from '../web-api-client';

export type SearchSortOrder = 'None' | 'PriceAsc' | 'PriceDesc';

@Injectable({
  providedIn: 'root'
})
export class SearchService {
  private readonly baseUrl: string;

  constructor(private http: HttpClient, @Optional() @Inject(API_BASE_URL) baseUrl?: string) {
    this.baseUrl = baseUrl ?? '';
  }

  search(q: string, page?: number, sort?: SearchSortOrder): Observable<SearchResultsVm> {
    let params = new HttpParams().set('q', q);

    if (page !== undefined) {
      params = params.set('page', page);
    }

    if (sort && sort !== 'None') {
      params = params.set('sort', sort);
    }

    return this.http
      .get<SearchResultsVm>(`${this.baseUrl}/api/search`, { params })
      .pipe(map(payload => SearchResultsVm.fromJS(payload)));
  }
}
