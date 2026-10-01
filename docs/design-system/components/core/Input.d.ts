export interface InputProps { value?: string; placeholder?: string; type?: string; disabled?: boolean; onChange?: (e: any) => void; className?: string; [k: string]: any; }
export declare function Input(props: InputProps): any;
export interface FieldProps { label: string; optional?: string; hint?: string; /** Error sentence: red border on the input, replaces the hint */ error?: string; children?: any; style?: Record<string, any>; }
export declare function Field(props: FieldProps): any;
