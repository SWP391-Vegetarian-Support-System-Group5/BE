# Backend Project Tracking

Last updated: 23 October 2026

This file is the repository-level source of truth while the organization has no public GitHub Project or assigned Issues. Update the status and verification evidence in the same pull request as the implementation.

| Work item | Owner | Status | Main endpoints / evidence | Remaining verification |
|---|---|---|---|---|
| Admin user dashboard and CRUD | An Ninh | Implemented | `/api/admin/dashboard/users`, `/api/admin/users` | MySQL integration test |
| Pre-publication AI moderation | An Ninh | Implemented | Posts and recipes use `PENDING_REVIEW`; admin approve/remove | Test with configured Gemini and MySQL |
| Post and comment moderation | Team | Implemented | `/api/admin/posts`, `/api/admin/comments`, `/api/admin/moderation` | End-to-end admin test |
| Allergens and diet-type status | An Ninh | Implemented | `/api/admin/allergens`, `/api/admin/diet-types` | Apply additive schema update |
| Restaurant and food management | An Ninh | Implemented | `/api/admin/restaurants/*`, nested `/foods` | Apply additive schema update and CRUD test |
| Unified search | An Ninh | Implemented | `/api/search` | Query against representative data |
| Admin image upload | An Ninh | Implemented | `/api/admin/uploads/images` | Deployment storage persistence decision |
| BMI recipe assignment | Team | Implemented | `/api/admin/recipes/bmi-types` | Existing test runner |
| Ingredient catalogue | Team | Implemented | `/api/ingredients`, admin ingredient endpoints | Existing test runner |
| Pagination for large admin lists | Unassigned | Planned | — | Define response envelope |
| Audit log for admin actions | Unassigned | Planned | — | Requires schema and actor/event model |
| Last-admin and self-lock protection | An Ninh | Implemented | Admin user update/status/delete guards | MySQL integration test |
| Rate limiting | Unassigned | Planned | — | Define limits for auth, OTP, search and map |

## Status definitions

- **Implemented**: code compiles and API contracts exist.
- **Verified**: automated or integration verification has passed against the team environment.
- **Planned**: accepted gap with no completed implementation.
- **Blocked**: cannot proceed without an external dependency or decision.

## Database note

Local development currently does not have the team MySQL database. The additive `AdminCatalogSchemaUpdate` runs only when database startup tasks are enabled. Do not mark database-backed work as **Verified** until the shared connection string is configured and the API tests pass without destructive schema changes.

## GitHub tracking links

- Issues: https://github.com/SWP391-Vegetarian-Support-System-Group5/BE/issues
- Pull requests: https://github.com/SWP391-Vegetarian-Support-System-Group5/BE/pulls
- Organization projects: https://github.com/orgs/SWP391-Vegetarian-Support-System-Group5/projects

