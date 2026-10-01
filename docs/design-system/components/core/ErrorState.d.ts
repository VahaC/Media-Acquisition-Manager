export interface ErrorStateProps {
  /** What failed, from the locale file: "Could not load the library." */
  title: string;
  /** The server's own sentence, shown as sent */
  children?: any;
  onRetry?: () => void;
  retryLabel?: string;
  retrying?: boolean;
  /** Lucide icon name, default CircleAlert */
  icon?: string;
  /** Extra action beside Retry, e.g. a link to Integrations */
  action?: any;
}
export declare function ErrorState(props: ErrorStateProps): any;
