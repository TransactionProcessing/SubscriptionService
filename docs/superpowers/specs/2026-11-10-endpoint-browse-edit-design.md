# Endpoint browse and edit workflow

## Intent

Make endpoint management open on the existing endpoint list, so operators can browse delivery targets before choosing to create or edit one. The approved screen direction is shown in [the issue #8 mockup](../../mockups/issue-8-endpoints.html).

## Goals

- `/admin/endpoints` displays the endpoint list without an empty editor.
- A clearly labeled **New endpoint** action opens `/admin/endpoints/new`.
- Each list item has an **Edit** action that opens `/admin/endpoints/{id}/edit`.
- The endpoint list uses compact aligned rows and offers search by endpoint name, URL, or authentication scheme.
- Create and edit pages group destination and authentication settings, with validation feedback adjacent to the relevant form.
- Cancel navigates back to the list without saving form changes.

## Non-goals

- Changing endpoint persistence, authentication semantics, or delivery behavior.
- Adding endpoint status controls or a new endpoint detail view.
- Changing the endpoint list's data source.

## Constraints

- Preserve the existing Blazor Server UI, shared application services, and endpoint editor model.
- The current `Endpoints.razor` handles the list, create form, and edit form. The list currently shares `/admin/endpoints` with the implicit create state; edit already has a dedicated route.

## Approaches

- **Separate routes (selected):** use `/admin/endpoints` for the list, add `/admin/endpoints/new` for creation, and retain the existing edit route. This matches the approved mockup and provides direct, bookmarkable URLs while keeping the list and form focused.
- **Drawer over the list:** keeps browsing context visible while editing, but introduces overlay and dismissal behavior that is not needed for this small workflow change.

## Implementation decisions

- Reuse the current endpoint list and editor model/service methods.
- Render list, create, and edit states conditionally from their routes.
- Return to `/admin/endpoints` after Cancel and successful save; retain existing delete behavior on edit.
- Keep authentication scheme and JSON parameters together in a clearly labeled optional section; retain current validation rules and present their feedback with the form.

## Open to the builder

- Exact markup and CSS details, responsive behavior, and how endpoint rows adapt the existing card styling.
- How to distinguish an unknown edit ID from an empty list using the existing not-found treatment.
