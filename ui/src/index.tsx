import { ModRegistrar } from "cs2/modding";
import { VanillaComponents } from "mods/vanilla-components";
import { extendMouseToolOptions } from "mods/guide-reach-section";

const register: ModRegistrar = (moduleRegistry) => {
  try {
    VanillaComponents.init(moduleRegistry);

    // Adds the "Guide line reach" section to the tool options panel (shown only for the road tool).
    moduleRegistry.extend(
      "game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx",
      "MouseToolOptions",
      extendMouseToolOptions
    );
  } catch (e) {
    // e.g. the vanilla module was renamed by a game update: keep the game UI working without our section.
    console.error("[SnapRangeControl] Failed to register the tool options UI.", e);
  }
};

export default register;
