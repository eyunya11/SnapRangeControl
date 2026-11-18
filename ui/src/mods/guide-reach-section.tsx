import { cloneElement, isValidElement, ReactElement, ReactNode } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { ModuleRegistryExtend } from "cs2/modding";
import { VanillaComponents } from "mods/vanilla-components";

const GROUP = "snapRangeControl";

// Values pushed from SnapRangeControlUISystem (C#).
const visible$ = bindValue<boolean>(GROUP, "visible", false);
const enabled$ = bindValue<boolean>(GROUP, "enabled", true);
const reach$ = bindValue<number>(GROUP, "reach", 512);
const minReach$ = bindValue<number>(GROUP, "minReach", 32);
const maxReach$ = bindValue<number>(GROUP, "maxReach", 512);

const GuideReachSection = () => {
  const visible = useValue(visible$);
  const enabled = useValue(enabled$);
  const reach = useValue(reach$);
  const minReach = useValue(minReach$);
  const maxReach = useValue(maxReach$);
  const { translate } = useLocalization();

  if (!visible || !VanillaComponents.available) {
    return null;
  }

  const { Section, ToolButton, mouseToolOptionsTheme: theme, FOCUS_DISABLED } = VanillaComponents;
  const t = (id: string, fallback: string) => translate(`SnapRangeControl.UI.${id}`, fallback) ?? fallback;

  return (
    <Section title={t("SectionTitle", "Guide line reach")}>
      <div style={{ display: "flex", flexDirection: "row", alignItems: "center" }}>
        <ToolButton
          src="Media/Tools/Snap Options/GuideLines.svg"
          selected={enabled}
          tooltip={t("ToggleTooltip", "Limit guide line reach (Snap Range Control)")}
          focusKey={FOCUS_DISABLED}
          onSelect={() => trigger(GROUP, "toggle")}
        />
        <ToolButton
          src="Media/Glyphs/ThickStrokeArrowDown.svg"
          disabled={!enabled || reach <= minReach}
          tooltip={t("DecreaseTooltip", "Decrease guide line reach")}
          focusKey={FOCUS_DISABLED}
          className={theme.startButton}
          onSelect={() => trigger(GROUP, "decrease")}
        />
        <div className={theme.numberField} style={{ justifyContent: "center", textAlign: "center" }}>
          {enabled ? `${reach} m` : t("Off", "Off")}
        </div>
        <ToolButton
          src="Media/Glyphs/ThickStrokeArrowUp.svg"
          disabled={!enabled || reach >= maxReach}
          tooltip={t("IncreaseTooltip", "Increase guide line reach")}
          focusKey={FOCUS_DISABLED}
          className={theme.endButton}
          onSelect={() => trigger(GROUP, "increase")}
        />
      </div>
    </Section>
  );
};

/**
 * Wraps the vanilla MouseToolOptions and appends our section inside its panel, after the vanilla sections.
 * The vanilla component ignores props.children and renders its own container, so we add our section to the
 * children of the element it returns. If its shape is unexpected, the vanilla output is returned unchanged.
 */
export const extendMouseToolOptions: ModuleRegistryExtend = (Component: any) => {
  if (typeof Component !== "function") {
    console.error("[SnapRangeControl] MouseToolOptions is not a function component; the tool options UI is not added.");
    return Component;
  }
  return (props: any) => {
    const result = Component(props);
    if (!isValidElement(result)) {
      return result;
    }
    const element = result as ReactElement<{ children?: ReactNode }>;
    return cloneElement(element, undefined, element.props.children, <GuideReachSection key="snapRangeControl" />);
  };
};
