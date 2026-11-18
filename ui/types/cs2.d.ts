// Minimal type declarations for the parts of the game's UI API that this mod uses.
// The game provides these modules at runtime (see "externals" in webpack.config.js); this file only lets
// TypeScript check the code in src/. Add declarations here when the mod starts using more of the API.

declare module "cs2/modding" {
  import { ComponentType } from "react";

  /** Wraps a vanilla component. The returned component replaces it. */
  export type ModuleRegistryExtend = <T extends ComponentType<any>>(curr: T) => (props: any) => JSX.Element;

  export type ModuleRegistry = {
    /** Vanilla modules by path. Each entry maps export names to values. */
    registry: Map<string, Record<string, any>>;
    extend(modulePath: string, exportName: string, extendCb: ModuleRegistryExtend): void;
  };

  /** The default export of the UI module. */
  export type ModRegistrar = (moduleRegistry: ModuleRegistry) => void;
}

declare module "cs2/api" {
  /** A handle to a C# ValueBinding. Read it with useValue; its fields are not used directly. */
  export interface ValueBinding<T> {
    readonly __valueType?: T;
  }

  /** Binds to a C# ValueBinding<T> registered under (group, name). */
  export function bindValue<T>(group: string, name: string, fallbackValue?: T): ValueBinding<T>;

  /** Calls a C# TriggerBinding registered under (group, name). */
  export function trigger(group: string, name: string, ...args: any[]): void;

  /** Subscribes a component to a binding and returns its current value. */
  export function useValue<T>(binding: ValueBinding<T>): T;
}

declare module "cs2/l10n" {
  export interface Localization {
    translate(id: string, fallback?: string | null): string | null;
  }

  export function useLocalization(): Localization;
}
