/**
 * @startingPoint section="Core" subtitle="Card with header, description and content" viewport="700x300"
 */
export interface CardProps { title?: string; description?: string; headerEnd?: any; footer?: any; children?: any; as?: string; padded?: boolean; className?: string; style?: Record<string, any>; }
export declare function Card(props: CardProps): any;
