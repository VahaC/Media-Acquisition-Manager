export interface SelectOption { value: string; label: string; }
export interface SelectProps { options: (string | SelectOption)[]; value?: string; onChange?: (e: any) => void; className?: string; [k: string]: any; }
export declare function Select(props: SelectProps): any;
