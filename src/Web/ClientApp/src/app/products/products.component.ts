import { Component } from '@angular/core';
import { SearchResultItem, SearchResultsVm } from '../web-api-client';
import { SearchService, SearchSortOrder } from '../search/search.service';

@Component({
  selector: 'app-products',
  templateUrl: './products.component.html',
  styleUrls: ['./products.component.scss']
})
export class ProductsComponent {
  query = '';
  page = 1;
  sort: SearchSortOrder = 'None';
  loading = false;
  error: string | null = null;
  results: SearchResultsVm | null = null;

  constructor(private searchService: SearchService) {}

  search(): void {
    const trimmed = this.query.trim();
    if (!trimmed) {
      this.error = 'Ingresa un término de búsqueda.';
      this.results = null;
      return;
    }

    const page = this.page && this.page > 0 ? this.page : undefined;

    this.loading = true;
    this.error = null;

    const sort = this.sort && this.sort !== 'None' ? this.sort : undefined;

    this.searchService.search(trimmed, page, sort).subscribe({
      next: result => {
        this.results = result;
      },
      error: err => {
        this.error = this.formatError(err);
        this.loading = false;
      },
      complete: () => {
        this.loading = false;
      }
    });
  }

  nextPage(): void {
    this.page = (this.page || 1) + 1;
    this.search();
  }

  previousPage(): void {
    if ((this.page || 1) <= 1) {
      return;
    }

    this.page = (this.page || 1) - 1;
    this.search();
  }

  formatPrice(item: SearchResultItem): string {
    if (item.priceAmount === undefined || item.priceAmount === null) {
      return 'Precio no disponible';
    }

    if (item.currency) {
      try {
        return new Intl.NumberFormat('es-ES', {
          style: 'currency',
          currency: item.currency
        }).format(item.priceAmount);
      } catch {
        // Fallback for unknown currency codes
      }
    }

    return `${item.priceAmount}`;
  }

  private formatError(err: any): string {
    if (!err) {
      return 'Error inesperado al buscar.';
    }

    if (typeof err === 'string') {
      return err;
    }

    if (err.message) {
      return err.message;
    }

    if (err.response) {
      return err.response;
    }

    return 'Error inesperado al buscar.';
  }
}
