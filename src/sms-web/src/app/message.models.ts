export const MESSAGE_STATUSES = ['Pending', 'Sending', 'Sent', 'Failed'] as const;
export type MessageStatus = (typeof MESSAGE_STATUSES)[number];

export interface SendMessageRequest {
  to: string;
  body: string;
}

export interface SendMessageResponse {
  id: string;
  status: MessageStatus;
}

export interface MessageListItem {
  id: string;
  to: string;
  status: MessageStatus;
}

export interface MessageList {
  items: MessageListItem[];
}

/**
 * RFC 9457 problem returned by the API on errors.
 * On 400 for a format error, `errors` holds the failing fields. For a number that belongs to no employee
 * the 400 is generic on purpose ("Invalid input", no `errors`).
 */
export interface Problem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}
