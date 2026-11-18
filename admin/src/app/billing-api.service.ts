import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Customer { id: string; email: string; name: string; stripeCustomerId: string; createdAt: string; }
export interface Subscription {
  id: string; customerId: string; planCode: string; stripeSubscriptionId: string;
  status: string; createdAt: string; canceledAt: string | null;
}
export interface Invoice {
  id: string; stripeInvoiceId: string; customerId: string; amountDueMinor: number; amountPaidMinor: number;
  currency: string; status: string; failedAttempts: number; createdAt: string; paidAt: string | null;
}
export interface LedgerEntry {
  id: string; transactionId: string; account: string; direction: 'Debit' | 'Credit';
  amountMinor: number; currency: string; sourceReference: string; postedAt: string;
}
export interface LedgerSummary {
  balances: { account: string; currency: string; balanceMinor: number }[];
  isBalanced: boolean;
}

export interface Connection { baseUrl: string; apiKey: string; }

/** Read-only client for the billing API. The key lives only in this browser tab's session storage. */
@Injectable({ providedIn: 'root' })
export class BillingApiService {
  private readonly http = inject(HttpClient);
  private static readonly storageKey = 'stripe-billing-admin';

  connection: Connection = this.load();

  save(connection: Connection): void {
    this.connection = { baseUrl: connection.baseUrl.replace(/\/+$/, ''), apiKey: connection.apiKey.trim() };
    sessionStorage.setItem(BillingApiService.storageKey, JSON.stringify(this.connection));
  }

  customers(): Observable<Customer[]> { return this.get('customers'); }
  subscriptions(): Observable<Subscription[]> { return this.get('subscriptions'); }
  invoices(): Observable<Invoice[]> { return this.get('invoices'); }
  ledger(): Observable<LedgerEntry[]> { return this.get('ledger'); }
  ledgerSummary(): Observable<LedgerSummary> { return this.get('ledger/summary'); }

  private get<T>(path: string): Observable<T> {
    const headers = new HttpHeaders({ 'X-Api-Key': this.connection.apiKey });
    return this.http.get<T>(`${this.connection.baseUrl}/api/${path}?take=100`, { headers });
  }

  private load(): Connection {
    try {
      const raw = sessionStorage.getItem(BillingApiService.storageKey);
      if (raw) { return JSON.parse(raw) as Connection; }
    } catch { /* storage unavailable: fall back to defaults */ }
    return { baseUrl: 'http://localhost:5080', apiKey: '' };
  }
}
