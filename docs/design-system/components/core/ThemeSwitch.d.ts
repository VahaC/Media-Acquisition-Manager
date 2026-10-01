export interface ThemeSwitchProps {
  /** Controlled mode; omit to let the switch read/write localStorage `mam:theme` itself */
  value?: "light" | "dark" | "system";
  onChange?: (mode: "light" | "dark" | "system") => void;
  /** Show "Light / Dark / System" text beside the icons */
  showLabels?: boolean;
  size?: "sm" | "default";
}
export declare function ThemeSwitch(props: ThemeSwitchProps): any;
