export interface MatchBadgesProps { evidence: "Identifier" | "TitleAndYear" | "TitleOnly"; schemes?: string[]; matchedTitle?: string; conflicts?: string[]; /** on a release row: names the work on hover */ inLibraryOf?: string; }
export declare function MatchBadges(props: MatchBadgesProps): any;
