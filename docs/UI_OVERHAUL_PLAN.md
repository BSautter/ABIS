# UI Overhaul + Login / RBAC — Plan (post-build)

**Status: captured direction, POST-BUILD.** Sequence this after the remaining building/imports
+ the parity-guard batch (`docs/PARITY_GUARD_WORKLIST.md`) — it's an integration + polish phase,
not blocking. Requirement (user, 2026-07-08): (1) a UX/redesign pass, and (2) a start-of-app
**login** that gates what a user can access or even see by their role/feature grants (RBAC-driven
UI, not just server-side rejection).

## Scope = all three UI surfaces
Each has a distinct UX profile the redesign must respect; they share one design system + auth/RBAC layer.
- **ABIS** — main ERP/MES web modules (~34 pages); office/desktop, dense forms + reports.
- **DAS** — shop-floor data-acquisition screens (`legacy/src/da/`); line-side/kiosk → large touch
  targets, glanceable live status, minimal chrome, gloves/dirty-screen tolerant.
- **Scanner** — handheld RF coil-receiving (`legacy/web/.../coil_receiving*.pl`); tiny screen,
  scan-first single-field flows, one-handed.

## Login / RBAC — the server-side half is already done (PR #82)
Don't reinvent the permission model; the UI consumes it.
- Enforcement: `RequireFeatureAsync` + the app-wide `/api` mutation gate mirroring legacy
  `f_security_door` (0 ReadOnly / 1 Write / <0 None); API-key service accounts bypass.
- UI-driving reads: `GET /api/security/me/permissions` and
  `GET /api/security/me/allowed?feature=&level=` — built to drive **hide / read-only / enable**.
- **Still missing:** the OIDC login flow — browser `Auth:Oidc` via the anonymous `GET /auth/config`,
  mapping the OIDC login → `security_user.login_id` (see NEXT_STEPS.md §2). Then resolve the caller's
  effective permissions on load and shape nav/controls per grant. Note: the tag→feature map
  (`FeatureByTag`) has deferred/ambiguous mappings to finish, and reads are currently ungated.

## Design direction the user liked (references 2026-07-08)
Two dashboard mockups (an "ABIS Integrated Operations" concept + the EDIGenerator/Aayu product) and a
Zebra "ABIS – Coil Scanner" mock. Distilled patterns (primarily the **ABIS desktop** surface; adapt
density for DAS/scanner):
- **Shell:** dark left sidebar nav (icon+label, collapsible groups); top bar with global search
  (typeahead over POs/work orders/EDI), notifications, settings, and a **user chip showing name + role**
  (RBAC surfaced in the header).
- **Layout:** card-based, sectioned dashboard with descriptive section headers; white cards on light-gray,
  rounded corners, generous whitespace.
- **KPIs:** big-number stat cards with trend/✓ badges; **donut charts** by EDI transaction type
  (204/850/810/880/997).
- **MES:** live production-floor grid/node map with Running(green)/Idle(yellow)/Stopped(red) states.
- **EDI:** transaction timeline (gantt) + an **Exception Queue** table with inline Quick Actions
  (Retry / View Details).
- **Tables:** legible, dense-but-clean, vendor avatars, **status badges/chips**, in-table search,
  row-level action buttons; side panels for alerts (inventory below reorder point).

## Scanner surface specifics (Zebra handheld)
A tall single-column, step-driven flow for a ~5" gloved touchscreen: title bar (logo + screen + print);
a big **"Step 1: Scan Coil Barcode"** viewport; a **Scanned Coil ID** field (waiting → populated);
a **"Verified from ERP"** card (Order/Vendor/status color-coded/Weight/✓) + a "Ready for import" pill;
then **3 large color-coded buttons** — Print Inventory Label / Print Import Label / Import & All Labels.
Maps to real API paths: scan → GET coil/receiving-bol → verify → `POST /api/receiving-bols/{id}/mint` + print.
- **Scan input:** Zebra **DataWedge** feeds the barcode as keystrokes into the focused field (or intent);
  camera reticle is a fallback.
- **Label printing:** the Zebra thermal printer needs **ZPL/CPCL** — NOT the HTML coil-label doc
  (`GET /api/documents/coil-label` is browser/PDF). **ZPL label generation is still a gap**
  (PARITY_AUDIT flags "coil ZPL/CPCL labels") — its own task.
- **Delivery:** a mobile-web/PWA served to the device browser (no app-store deploy; reuses the same
  API + design tokens as the ABIS desktop app).

## When it starts
Synthesize into a design-system + clickable mockups for all three shells (ABIS dashboard, DAS kiosk board,
scanner flow) bound to real ABIS data shapes/roles. Before OIDC GA: finish the `FeatureByTag` mappings and
decide whether to default-deny unmapped mutations + gate reads.
