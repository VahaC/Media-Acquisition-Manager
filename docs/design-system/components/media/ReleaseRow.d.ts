export interface ReleaseClaim { resolution?: number; medium?: string; codec?: string; ranges?: string[]; audio?: string[]; subs?: string[]; }
export interface Release { title: string; source: string; size?: string; seeders?: number; peers?: number; published?: string; claimed?: ReleaseClaim; match?: { evidence: "Identifier" | "TitleAndYear" | "TitleOnly"; work: string; conflicts?: string[] }; }
/**
 * @startingPoint section="Media" subtitle="A release row from Search" viewport="700x220"
 */
export interface ReleaseRowProps { release: Release; onDetails?: () => void; downloadEnabled?: boolean; onDownload?: () => void; }
export declare function ReleaseRow(props: ReleaseRowProps): any;
