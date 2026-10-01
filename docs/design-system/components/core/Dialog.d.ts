export interface DialogProps { open?: boolean; title: string; description?: string; children?: any; footer?: any; onClose?: () => void; maxWidth?: string; /** render in flow without overlay (specimens) */ inline?: boolean; }
export declare function Dialog(props: DialogProps): any;
