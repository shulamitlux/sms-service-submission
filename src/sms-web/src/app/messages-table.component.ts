import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MESSAGE_STATUSES, MessageListItem, MessageStatus } from './message.models';
import { MessagesService } from './messages.service';

const REFRESH_MS = 3000;

const STATUS_LABELS: Record<MessageStatus, string> = {
  Pending: 'ממתינה',
  Sending: 'בשליחה',
  Sent: 'נשלחה',
  Failed: 'נכשלה',
};

@Component({
  selector: 'app-messages-table',
  imports: [FormsModule],
  template: `
    <section class="card">
      <div class="table-header">
        <h2>הודעות</h2>
        <label>
          סינון לפי סטטוס:
          <select [ngModel]="status()" (ngModelChange)="setStatus($event)">
            <option [ngValue]="null">הכל</option>
            @for (s of statuses; track s) {
              <option [ngValue]="s">{{ labels[s] }}</option>
            }
          </select>
        </label>
      </div>

      @if (loadError()) {
        <div class="alert error">{{ loadError() }}</div>
      }

      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>מספר הודעה</th>
              <th>טלפון</th>
              <th>סטטוס</th>
            </tr>
          </thead>
          <tbody>
            @for (m of messages(); track m.id) {
              <tr>
                <td><span dir="ltr">{{ m.id }}</span></td>
                <td><span dir="ltr">{{ m.to }}</span></td>
                <td>
                  <span [class]="'badge ' + m.status.toLowerCase()">{{ labels[m.status] }}</span>
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="3" class="muted center">אין הודעות</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
})
export class MessagesTableComponent {
  private readonly api = inject(MessagesService);

  readonly statuses = MESSAGE_STATUSES;
  readonly labels = STATUS_LABELS;
  readonly status = signal<MessageStatus | null>(null);
  readonly messages = signal<MessageListItem[]>([]);
  readonly loadError = signal<string | null>(null);

  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
    this.reload();
  }

  setStatus(status: MessageStatus | null): void {
    this.status.set(status);
    this.reload();
  }

  /** Loads the list and keeps refreshing every 3s: messages move between statuses, so any filter can change. */
  reload(): void {
    clearTimeout(this.timer);
    this.api.list(this.status()).subscribe({
      next: (list) => {
        this.loadError.set(null);
        this.messages.set(list.items);
        this.timer = setTimeout(() => this.reload(), REFRESH_MS);
      },
      error: () => this.loadError.set('טעינת ההודעות נכשלה'),
    });
  }
}
