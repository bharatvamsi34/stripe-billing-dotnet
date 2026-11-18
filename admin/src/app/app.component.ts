import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import {
  BillingApiService, Customer, Invoice, LedgerEntry, LedgerSummary, Subscription,
} from './billing-api.service';

type Tab = 'subscriptions' | 'invoices' | 'ledger' | 'customers';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [FormsModule, DatePipe],
  template: `
    <header>
      <div>
        <h1>Billing Admin</h1>
        <p class="muted">Read-only view of subscriptions, invoices and the payments ledger.</p>
      </div>
      <form class="connection" (ngSubmit)="connect()">
        <input name="baseUrl" [(ngModel)]="baseUrl" placeholder="API base URL" aria-label="API base URL" />
        <input name="apiKey" [(ngModel)]="apiKey" type="password" placeholder="API key" aria-label="API key" />
        <button type="submit">Connect</button>
      </form>
    </header>

    @if (error()) { <div class="alert" role="alert">{{ error() }}</div> }

    @if (summary(); as s) {
      <section class="cards">
        @for (b of s.balances; track b.account + b.currency) {
          <div class="card">
            <span class="muted">{{ label(b.account) }}</span>
            <strong>{{ money(Math.abs(b.balanceMinor), b.currency) }}</strong>
          </div>
        }
        <div class="card">
          <span class="muted">Ledger check</span>
          <strong [class.ok]="s.isBalanced" [class.bad]="!s.isBalanced">
            {{ s.isBalanced ? 'Balanced' : 'NOT balanced' }}
          </strong>
        </div>
      </section>
    }

    <nav>
      @for (t of tabs; track t) {
        <button [class.active]="tab() === t" (click)="tab.set(t)">{{ label(t) }}</button>
      }
    </nav>

    @if (loading()) { <p class="muted">Loading…</p> }

    @switch (tab()) {
      @case ('subscriptions') {
        <table>
          <thead><tr><th>Stripe ID</th><th>Plan</th><th>Status</th><th>Created</th></tr></thead>
          <tbody>
            @for (s of subscriptions(); track s.id) {
              <tr><td class="mono">{{ s.stripeSubscriptionId }}</td><td>{{ s.planCode }}</td>
                <td><span class="pill" [attr.data-status]="s.status">{{ s.status }}</span></td>
                <td>{{ s.createdAt | date: 'medium' }}</td></tr>
            } @empty { <tr><td colspan="4" class="muted">No subscriptions yet.</td></tr> }
          </tbody>
        </table>
      }
      @case ('invoices') {
        <table>
          <thead><tr><th>Stripe ID</th><th class="num">Due</th><th class="num">Paid</th><th>Status</th><th class="num">Failed attempts</th><th>Paid at</th></tr></thead>
          <tbody>
            @for (i of invoices(); track i.id) {
              <tr><td class="mono">{{ i.stripeInvoiceId }}</td>
                <td class="num">{{ money(i.amountDueMinor, i.currency) }}</td>
                <td class="num">{{ money(i.amountPaidMinor, i.currency) }}</td>
                <td><span class="pill" [attr.data-status]="i.status">{{ i.status }}</span></td>
                <td class="num">{{ i.failedAttempts }}</td>
                <td>{{ i.paidAt ? (i.paidAt | date: 'medium') : '—' }}</td></tr>
            } @empty { <tr><td colspan="6" class="muted">No invoices yet.</td></tr> }
          </tbody>
        </table>
      }
      @case ('ledger') {
        <table>
          <thead><tr><th>Posted</th><th>Account</th><th class="num">Debit</th><th class="num">Credit</th><th>Source</th></tr></thead>
          <tbody>
            @for (e of ledger(); track e.id) {
              <tr><td>{{ e.postedAt | date: 'medium' }}</td><td>{{ label(e.account) }}</td>
                <td class="num">{{ e.direction === 'Debit' ? money(e.amountMinor, e.currency) : '' }}</td>
                <td class="num">{{ e.direction === 'Credit' ? money(e.amountMinor, e.currency) : '' }}</td>
                <td class="mono">{{ e.sourceReference }}</td></tr>
            } @empty { <tr><td colspan="5" class="muted">No ledger entries yet.</td></tr> }
          </tbody>
        </table>
      }
      @case ('customers') {
        <table>
          <thead><tr><th>Name</th><th>Email</th><th>Stripe ID</th><th>Created</th></tr></thead>
          <tbody>
            @for (c of customers(); track c.id) {
              <tr><td>{{ c.name }}</td><td>{{ c.email }}</td><td class="mono">{{ c.stripeCustomerId }}</td>
                <td>{{ c.createdAt | date: 'medium' }}</td></tr>
            } @empty { <tr><td colspan="4" class="muted">No customers yet.</td></tr> }
          </tbody>
        </table>
      }
    }
  `,
})
export class AppComponent implements OnInit {
  private readonly api = inject(BillingApiService);
  protected readonly Math = Math;

  readonly tabs: Tab[] = ['subscriptions', 'invoices', 'ledger', 'customers'];
  readonly tab = signal<Tab>('subscriptions');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly customers = signal<Customer[]>([]);
  readonly subscriptions = signal<Subscription[]>([]);
  readonly invoices = signal<Invoice[]>([]);
  readonly ledger = signal<LedgerEntry[]>([]);
  readonly summary = signal<LedgerSummary | null>(null);

  baseUrl = this.api.connection.baseUrl;
  apiKey = this.api.connection.apiKey;

  ngOnInit(): void {
    if (this.apiKey) { this.refresh(); }
  }

  connect(): void {
    this.api.save({ baseUrl: this.baseUrl, apiKey: this.apiKey });
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      customers: this.api.customers(),
      subscriptions: this.api.subscriptions(),
      invoices: this.api.invoices(),
      ledger: this.api.ledger(),
      summary: this.api.ledgerSummary(),
    }).subscribe({
      next: (r) => {
        this.customers.set(r.customers);
        this.subscriptions.set(r.subscriptions);
        this.invoices.set(r.invoices);
        this.ledger.set(r.ledger);
        this.summary.set(r.summary);
        this.loading.set(false);
      },
      error: (e: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(e.status === 401 ? 'Invalid API key.' : `Could not reach the API (${e.status || 'network error'}).`);
      },
    });
  }

  money(amountMinor: number, currency: string): string {
    const code = currency.toUpperCase();
    const digits = new Intl.NumberFormat('en', { style: 'currency', currency: code }).resolvedOptions().maximumFractionDigits ?? 2;
    return new Intl.NumberFormat('en', { style: 'currency', currency: code }).format(amountMinor / 10 ** digits);
  }

  label(value: string): string {
    const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
  }
}
