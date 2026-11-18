import { ModuleRegistry } from "cs2/modding";

// Vanilla UI modules used by this mod (game 1.6.4f1). Looked up lazily from the module registry.
const MODULES = {
  Section: ["game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx", "Section"],
  ToolButton: ["game-ui/game/components/tool-options/tool-button/tool-button.tsx", "ToolButton"],
  mouseToolOptionsTheme: ["game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.module.scss", "classes"],
  FOCUS_DISABLED: ["game-ui/common/focus/focus-key.ts", "FOCUS_DISABLED"],
} as const;

type Key = keyof typeof MODULES;

let registry: ModuleRegistry | null = null;
const cache: Partial<Record<Key, any>> = {};

function get(key: Key): any {
  // Cache only found values, so a lookup that ran before the module was registered is retried next time.
  if (cache[key] === undefined) {
    const [path, exportName] = MODULES[key];
    cache[key] = registry?.registry.get(path)?.[exportName];
  }
  return cache[key];
}

export const VanillaComponents = {
  init(moduleRegistry: ModuleRegistry) {
    registry = moduleRegistry;
  },
  /** True when every vanilla module we need exists. If not (e.g. after a game update), the section is not shown. */
  get available(): boolean {
    return (Object.keys(MODULES) as Key[]).every((key) => get(key) !== undefined);
  },
  get Section(): any {
    return get("Section");
  },
  get ToolButton(): any {
    return get("ToolButton");
  },
  get mouseToolOptionsTheme(): Record<string, string> {
    return get("mouseToolOptionsTheme") ?? {};
  },
  get FOCUS_DISABLED(): any {
    return get("FOCUS_DISABLED");
  },
};
