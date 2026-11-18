import { ModRegistrar } from "cs2/modding";
import { VanillaComponents } from "mods/vanilla-components";
import { extendMouseToolOptions } from "mods/guide-reach-section";

const register: ModRegistrar = (moduleRegistry) => {
  VanillaComponents.init(moduleRegistry);

  // Adds the "Guide line reach" section to the tool options panel (shown only for the road tool).
  moduleRegistry.extend(
    "game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx",
    "MouseToolOptions",
    extendMouseToolOptions
  );
};

export default register;
