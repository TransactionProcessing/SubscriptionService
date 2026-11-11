# Endpoint Browse and Edit Implementation Plan

> **For agentic workers:** Use the native session to implement the single task below. Steps use checkbox syntax for tracking.

**Goal:** Make endpoint management open on a browse list and move creation and editing into focused routes.

**Architecture:** Keep the existing Blazor endpoint list page and admin service. Add a separate endpoint editor component for `/admin/endpoints/new` and `/admin/endpoints/{EndpointId:int}/edit` so navigation always switches between distinct routed components.

**Tech Stack:** .NET, Blazor Razor components, existing SubscriptionAdminService.

**Spec:** `docs/superpowers/specs/2026-11-10-endpoint-browse-edit-design.md`

## Global Constraints

- Preserve the existing Blazor Server UI, shared application services, and endpoint editor model.
- Do not change endpoint persistence, authentication semantics, or delivery behavior.
- Cancel navigates back to `/admin/endpoints` without saving form changes.

## Review Focus

- Empty endpoint collection: show an empty list state and keep New endpoint available.
- Search with no matches: show a clear no-match state; keep search limited to name, URL, and authentication scheme.
- New route: initialize a blank model and do not show the list beside the editor.
- Edit route with missing ID: show the existing not-found treatment.
- Validation and save errors: keep feedback understandable alongside the focused form.
- Cancel: return to the list without invoking save.

---

### Task 1: Split endpoint list and editor routes

**Files:**
- Modify: `src/SubscriptionService.Worker/Components/Pages/Endpoints.razor`
- Create: `src/SubscriptionService.Worker/Components/Pages/EndpointEditor.razor`

**Interfaces:**
- Consumes: existing `SubscriptionAdminService` methods and `EndpointEditorModel`.
- Produces: list route `/admin/endpoints` in `Endpoints.razor`, and create/edit routes `/admin/endpoints/new` and `/admin/endpoints/{EndpointId:int}/edit` in `EndpointEditor.razor`.

- [x] Keep only the list route in `Endpoints.razor`; add the `/admin/endpoints/new` and edit routes to the distinct `EndpointEditor.razor` component.
- [x] Point New endpoint to the create route and retain Edit links to the existing edit route.
- [x] Render endpoint data in compact aligned rows and filter visible rows by name, URL, or authentication scheme.
- [x] Keep authentication fields and validation feedback grouped, preserve save/delete behavior, and route Cancel to the list without saving.
- [x] Review the resulting Razor markup against the approved mockup and the route-specific behaviors listed in Review Focus.
