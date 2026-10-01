export interface BannerProps { tone?: "error" | "warning" | "ok"; children?: any; /** e.g. a Retry button on the right */ action?: any; onDismiss?: () => void; }
export declare function Banner(props: BannerProps): any;
export interface StatusLineProps { status?: "ok" | "error" | "warning" | "running"; children?: any; as?: string; }
export declare function StatusLine(props: StatusLineProps): any;
