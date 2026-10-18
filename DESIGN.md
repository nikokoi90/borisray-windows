---
name: BorisRay for Windows
description: The inherited gold manta identity in a native Windows VPN utility.
colors:
  gold: "#F1C27D"
  ground: "#101014"
  sidebar: "#19191E"
  input: "#202024"
  text: "#F4F1EC"
  secondary: "#B7B7C0"
  selected: "#413426"
  action-border: "#414149"
  grid-divider: "#3C3C43"
  error: "#FF9E9E"
  error-ground: "#321C1F"
typography:
  display:
    fontFamily: "Georgia"
    fontSize: "20pt"
    fontWeight: 700
  headline:
    fontFamily: "Segoe UI"
    fontSize: "23pt"
    fontWeight: 700
  title:
    fontFamily: "Segoe UI"
    fontSize: "14pt"
    fontWeight: 700
  status:
    fontFamily: "Segoe UI"
    fontSize: "18pt"
    fontWeight: 700
  body:
    fontFamily: "Segoe UI"
    fontSize: "10pt"
    fontWeight: 400
  label:
    fontFamily: "Segoe UI"
    fontSize: "10pt"
    fontWeight: 700
  caption:
    fontFamily: "Segoe UI"
    fontSize: "9pt"
    fontWeight: 400
rounded:
  action: "8px"
spacing:
  field-label: "6px"
  row-bottom: "8px"
  action-gap: "10px"
  text-bottom: "12px"
  field-gap: "16px"
  page-vertical: "24px"
  page-left: "28px"
components:
  button-primary:
    backgroundColor: "{colors.gold}"
    textColor: "{colors.ground}"
    typography: "{typography.body}"
    rounded: "{rounded.action}"
    padding: "6px 12px"
  button-secondary:
    backgroundColor: "{colors.input}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
    rounded: "{rounded.action}"
    padding: "6px 12px"
  button-connect:
    backgroundColor: "{colors.gold}"
    textColor: "{colors.ground}"
    typography: "{typography.label}"
    rounded: "{rounded.action}"
    padding: "6px 12px"
  button-disabled:
    backgroundColor: "{colors.input}"
    textColor: "{colors.secondary}"
    rounded: "{rounded.action}"
  navigation-item:
    backgroundColor: "{colors.sidebar}"
    textColor: "{colors.secondary}"
    typography: "{typography.body}"
    rounded: "{rounded.action}"
    height: "44px"
    width: "198px"
  navigation-item-selected:
    backgroundColor: "{colors.selected}"
    textColor: "{colors.gold}"
    typography: "{typography.body}"
    rounded: "{rounded.action}"
    height: "44px"
    width: "198px"
  input:
    backgroundColor: "{colors.input}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
  rule-row:
    backgroundColor: "{colors.ground}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
    padding: "4px"
    height: "40px"
  rule-row-selected:
    backgroundColor: "{colors.selected}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
    padding: "4px"
    height: "40px"
---

# Design System: BorisRay for Windows

## Overview

**Creative North Star: "The inherited gold manta"**

BorisRay keeps the approved original golden manta and English serif wordmark inside a quiet charcoal utility. The image supplies material character; the surrounding controls stay flat, compact and readable. Gold identifies the brand, the active navigation item and the principal action. It is not an all-purpose decoration.

The Windows expression pairs that existing identity with native Segoe UI text, keyboard focus, selection controls, editable tables and resizable scrolling forms. State and explanatory copy sit beside the affected action. The interface communicates a free client for the user's own subscription without imposing a provider identity.

This baseline describes `src/BorisRay.Windows/MainWindow.cs` at source revision `28083d2`, with DPI setup in `src/BorisRay.Windows/Program.cs`. The final native capture packet in `.impeccable/review/windows/` contains 20 views at 96 DPI; its `ci.json` identifies the same revision. It establishes the palette, composition, compact layout, keyboard focus and pending-route guidance. The six pending views include explicitly simulated running states without a network connection. Documentation evidence and validation are recorded in `.impeccable/review/windows-documentation.json`. Physical Windows 11 tunnel behavior and high-DPI appearance remain unverified. HTML specimens in the sidecar are portable illustrations of native components, not application code or Windows acceptance evidence.

**Key Characteristics:**
- Original golden manta raster with a gold Georgia wordmark.
- Persistent charcoal sidebar and a flat near-black work area.
- One dominant connection action with adjacent operating state.
- Native fields and direct table editing with visible keyboard focus.
- Saved route changes explained where they are edited.

## Colors

Warm gold and warm pale text sit on three close charcoal surfaces; a muted brown selection ties navigation and tables to the accent.

### Primary

- **Manta Gold** (`colors.gold`): the wordmark, primary actions, active navigation text and running status in the sidebar. The principal action uses ground-colored text on gold.

### Neutral

- **Near-black Ground** (`colors.ground`): the main work area, table body and ink on a gold action.
- **Sidebar Charcoal** (`colors.sidebar`): the persistent navigation field and table headers.
- **Input Charcoal** (`colors.input`): secondary actions, text fields and native selection controls; also the disabled action fill.
- **Warm Text** (`colors.text`): headings, body copy, table cells and secondary action labels.
- **Quiet Gray** (`colors.secondary`): explanations, inactive navigation, off-state sidebar text and disabled action labels.
- **Selected Umber** (`colors.selected`): selected navigation background, selected table rows and selected owner-drawn combo items.
- **Action Edge** (`colors.action-border`): the actual owner-painted secondary button outline. The separate native `FlatAppearance.BorderColor` property does not determine this painted edge.
- **Grid Divider** (`colors.grid-divider`): native table grid color, with horizontal cell dividers.

Error feedback uses **Soft Error** (`colors.error`) on **Error Ground** (`colors.error-ground`) in a full-width top banner. These are semantic feedback colors rather than a second brand accent.

**The Gold Anchor Rule.** Keep the inherited gold on the identity, active navigation and principal action; use charcoal secondary actions for supporting operations.

## Typography

**Display Font:** Georgia, bold, for the English BorisRay wordmark.

**Body Font:** Segoe UI, the native Windows interface family.

**Character:** The serif wordmark carries the inherited brand while the interface uses clear Windows typography. Native font metrics determine line height and wrapping; the implementation specifies no custom tracking or line-height scale.

### Hierarchy

- **Display** (`typography.display`): the sidebar wordmark.
- **Headline** (`typography.headline`): the page title at the top of each work area.
- **Title** (`typography.title`): recurring subscription, measurement and About section headings.
- **Status** (`typography.status`): the prominent connection state beside the connection action.
- **Body** (`typography.body`): controls, tables, explanatory copy and pending-route guidance.
- **Label** (`typography.label`): field labels, table headings and the connection button.
- **Caption** (`typography.caption`): the version/free-client line in the sidebar footer.

Font sizes in the frontmatter are points because `System.Drawing.Font` uses points. Layout values below are native pixels at the 96 DPI design baseline. The About page also uses a local 16pt product heading and the application chooser a local 13pt filename; neither establishes a new reusable type role.

**The Two Voices Rule.** Keep Georgia on the brand wordmark and Segoe UI on operating text and controls.

## Layout

The native window starts with a client area of 1100 by 780 pixels and an outer minimum size of 930 by 640 pixels. A left-docked sidebar is 232 pixels wide at the design baseline. Its inset is 16 pixels horizontally, 22 at the top and 18 at the bottom. The brand row is 80 pixels high and displays the original raster at 48 by 48 pixels beside the wordmark. Navigation uses a vertical stack of 198 by 44 pixel buttons; the state and version sit at the bottom.

The main work area is a vertically stacked `FlowLayoutPanel`, inset 28 pixels from the left and 24 on the other sides. It has no card grid. Horizontal action and field rows wrap when their available width narrows. Recurring spacing is recorded in the frontmatter: small label-to-field separation, compact row gaps, a larger gap between fields and a consistent text bottom margin.

Country and channel use paired 270 pixel selection fields. The server selector prefers 590 pixels. Rows and tables prefer a maximum width of 780 pixels, while labels begin with a 750 pixel maximum. `AdaptContent()` recalculates available width from the actual sidebar and viewport, wraps labels and rows, and fits tables, multiline fields and combo boxes to the available space. The native outer work area scrolls vertically when needed. No web or mobile breakpoint exists.

Application and site tables are 300 pixels high; their rows are 40 pixels with 36 pixel headers. The application page exposes the selected executable's full path in a 54 pixel read-only multiline field. Connections use a 100 pixel details field beneath the table. Native title bars, scrollbars, checkboxes and field chrome remain part of the Windows expression.

The source enables per-monitor V2 DPI mode and form DPI scaling from 96 DPI. The supplied captures establish only 96 DPI output; scaling support is an implementation fact, not verified high-DPI appearance.

## Elevation & Depth

The utility has no custom shadow vocabulary or animated elevation. Depth comes from adjacent charcoal tones, a quiet selected fill and restrained control edges. The original dimensional manta raster is the only material image. Native Windows field chrome and window decoration are retained; they are not converted into custom flat controls.

**The Flat Utility Rule.** Keep the work area and navigation flat; let tonal separation, selection and native control feedback carry hierarchy.

## Shapes

Owner-painted actions and navigation items have gently rounded corners (`rounded.action`), drawn with 16 pixel diameter corner arcs. Primary gold actions have no painted outline. Secondary actions use a one-pixel Action Edge outline. Navigation is borderless. Native text fields, selection controls and data grids retain their own rectangular shapes rather than inheriting the button radius.

The connection action's power mark is drawn geometry: a stroked arc and line. It is not a text glyph or an icon-font character. The original manta asset is embedded from `assets/borisray.png`; preserve that asset rather than redrawing it as a symbol.

## Components

### Buttons

Compact native actions with a clear gold primary variant and charcoal supporting variant.

- **Shape and spacing:** use the action radius and component inset in the frontmatter. Ordinary actions have a 38 pixel minimum height. The connection action has a 208 by 54 pixel minimum and bold body text with a geometric power mark.
- **Primary:** gold fill with ground-colored text. Empty-state subscription import becomes the primary action; the connection action remains disabled until a usable profile exists.
- **Secondary:** input-colored fill, warm text and the actual painted action outline. Supporting route, refresh, file, delete and diagnostic operations use this vocabulary.
- **Hover and pressed:** event-driven redraw uses `ControlPaint.Light(fill, .08f)` on hover and `ControlPaint.Dark(fill, .1f)` when pressed. There is no timed transition or entrance animation.
- **Focus:** when keyboard focus cues are active, a two-pixel dotted inset rectangle uses ground on a gold button and gold on other buttons. Preserve the native Tab order and visible focus.
- **Disabled and busy:** disabled actions use the input fill and quiet gray text. Exclusive work disables the content area and changes the sidebar state to the ongoing-action message.

### Inputs / Fields

Dark native fields keep Windows editing and selection behavior.

- Text fields use the input fill, warm text and native borders. Field labels are bold and directly above the input.
- Combo boxes are native `DropDownList` controls with owner-drawn 27 pixel items. Text has a nine-pixel left inset; selected items use Selected Umber and native focus feedback. Native closed-field and drop-arrow chrome is retained.
- The subscription import field is password-masked by default. A visible checkbox controls disclosure; the dialog has native confirm and cancel keyboard bindings.
- Validation feedback uses the top error banner with plain explanatory text. It does not replace input values with a fabricated success state.

### Navigation

A persistent five-item sidebar uses left-aligned labels. Inactive items have the sidebar fill and quiet gray text; the active item has the selected fill and gold text. It shares button hover, pressed and keyboard focus behavior. The footer state remains separate from page selection. There is no collapsed or mobile navigation variant.

### Editable Route Tables

Flat native `DataGridView` tables expose the route decision in place. Use bold sidebar-colored headers, warm text, horizontal dividers and the umber full-row selection. Column widths flex with the available work area; the selected application's full path remains accessible beneath the grid even when its table cell is truncated.

The application table keeps name and path read-only while the route is a native combo cell and folder scope is a checkbox cell. The site table keeps the domain read-only and the route editable. Edits commit immediately into saved settings. Native table selection, scrolling and keyboard editing remain functional behavior, not a static illustration.

### Connection Action and Pending Routes

The top connection action changes its text with engine state and places a prominent state label next to it. Unmeasured latency and speed stay as dashes; connection tables use explicit empty or unavailable messages.

Connection, Applications and Sites each expose saved route changes. When stopped, the guidance says that rules apply on the next connection. When running, it says that rules are saved and require reconnection, with a visible **Применить маршруты** action. That action actually stops the engine and starts it again; a successful start clears the pending state. `PendingRoutes()` supplies the shared editing-page pattern. Synthetic connected capture fixtures establish its rendering only and do not establish a working tunnel.

**The Saved Versus Applied Rule.** Keep pending guidance on the surface where routes are edited until a successful connection applies the saved configuration.

## Do's and Don'ts

### Do:

- **Do** preserve the original golden manta and gold Georgia BorisRay wordmark.
- **Do** use native Segoe UI controls, keyboard focus and table editing for operating tasks.
- **Do** retain one clear principal action and use charcoal for supporting operations.
- **Do** wrap and scroll within the actual available native work area.
- **Do** explain saved route changes on Connection, Applications and Sites until they are applied.
- **Do** keep unknown measurements and inactive connection states explicit.

### Don't:

- **Don't** introduce BorisHub or provider branding into this free client.
- **Don't** redraw the inherited manta as a glyph or decorate operating screens with a card grid.
- **Don't** apply the action radius to native fields and tables.
- **Don't** add decorative shadows or timed entrance motion to the flat utility system.
- **Don't** imply that saving a route has applied it to a running tunnel.
- **Don't** treat synthetic native captures, generated HTML specimens or DPI source settings as physical Windows 11 network or high-DPI acceptance.
