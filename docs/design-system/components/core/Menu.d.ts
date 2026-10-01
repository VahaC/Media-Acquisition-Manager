export interface MenuItem { label?: string; icon?: string; danger?: boolean; disabled?: boolean; separator?: boolean; onClick?: () => void; }
export interface MenuProps { items: MenuItem[]; style?: Record<string, any>; className?: string; }
export declare function Menu(props: MenuProps): any;
