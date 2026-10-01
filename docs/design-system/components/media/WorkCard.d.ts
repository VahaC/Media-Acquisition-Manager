export interface MediaFacts { duration?: string; resolution?: string; codec?: string; range?: string; bitrate?: string; audio?: string[]; subs?: string[]; }
export interface WorkFile { name: string; folder: string; appPath?: string | null; size?: string; location?: string; media?: MediaFacts; }
export interface WorkVersion { label?: string; files: WorkFile[]; }
export interface Work { title: string; originalTitle?: string; year?: number; source?: string; ids?: string[]; missing?: string[]; versions: WorkVersion[]; }
/**
 * @startingPoint section="Media" subtitle="A library work with versions and verified facts" viewport="700x400"
 */
export interface WorkCardProps { work: Work; evidence?: any; headingLevel?: 2 | 3; }
export declare function WorkCard(props: WorkCardProps): any;
