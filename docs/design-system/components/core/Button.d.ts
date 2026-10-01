/**
 * @startingPoint section="Core" subtitle="Primary, outline, ghost, danger buttons" viewport="700x260"
 */
export interface ButtonProps { variant?: "default" | "outline" | "secondary" | "ghost" | "link" | "destructive" | "danger"; size?: "default" | "sm" | "lg" | "icon"; /** Lucide icon name before the label */ icon?: string; iconRight?: string; loading?: boolean; disabled?: boolean; title?: string; type?: "button" | "submit"; onClick?: (e: any) => void; className?: string; children?: any; }
export declare function Button(props: ButtonProps): any;
