# Role Permissions Matrix

**Document:** Authentication and Authorization Reference
**US:** US-003
**Date:** 2026-03-19

---

## Overview

CampaignEngine uses **ASP.NET Core Identity** with **claims-based role authorization**.
Three roles are defined, each granting access to a specific feature subset.
A **FallbackPolicy** requires authentication for all pages and endpoints not explicitly decorated with `[AllowAnonymous]`.

---

## Roles

| Role       | Description                                                                 | Default For New Users |
|------------|-----------------------------------------------------------------------------|----------------------|
| `Admin`    | Full access to all features, user management, and system configuration.     | No                   |
| `Designer` | Template CRUD and preview. No campaign access.                              | No                   |
| `Operator` | Full campaign CRUD and monitoring. Read-only template access.               | **Yes**              |

---

## Feature Access Matrix

| Feature Area                      | Admin | Designer | Operator |
|-----------------------------------|:-----:|:--------:|:--------:|
| **Templates**                     |       |          |          |
| Create template                   |  YES  |   YES    |    NO    |
| Edit template                     |  YES  |   YES    |    NO    |
| Delete (soft) template            |  YES  |   YES    |    NO    |
| Preview template                  |  YES  |   YES    |    NO    |
| View/read templates               |  YES  |   YES    |   YES    |
| Publish / archive template        |  YES  |   YES    |    NO    |
| **Campaigns**                     |       |          |          |
| Create campaign                   |  YES  |    NO    |   YES    |
| Edit campaign                     |  YES  |    NO    |   YES    |
| Launch / schedule campaign        |  YES  |    NO    |   YES    |
| Monitor campaign progress         |  YES  |    NO    |   YES    |
| Cancel campaign                   |  YES  |    NO    |   YES    |
| **Data Sources**                  |       |          |          |
| Create / edit data source         |  YES  |    NO    |    NO    |
| View data sources                 |  YES  |   YES    |   YES    |
| **User Management**               |       |          |          |
| View users                        |  YES  |    NO    |    NO    |
| Create user                       |  YES  |    NO    |    NO    |
| Assign / change roles             |  YES  |    NO    |    NO    |
| Activate / deactivate user        |  YES  |    NO    |    NO    |
| **System Configuration**          |       |          |          |
| SMTP / SMS provider configuration |  YES  |    NO    |    NO    |
| Hangfire dashboard                |  YES  |    NO    |    NO    |
| View audit logs                   |  YES  |    NO    |    NO    |
| API key management                |  YES  |    NO    |    NO    |

---

## Authorization Policies

Policies are registered in `CampaignEngine.Application.DependencyInjection.AuthorizationPolicies`
and configured in `ServiceCollectionExtensions.AddApplication()`.

| Policy Name               | Allowed Roles            | Usage Example                        |
|---------------------------|--------------------------|--------------------------------------|
| `RequireAdmin`            | Admin                    | User management, system config       |
| `RequireDesigner`         | Designer                 | Template edit forms                  |
| `RequireOperator`         | Operator                 | Campaign creation forms              |
| `RequireDesignerOrAdmin`  | Designer, Admin          | Template publish/archive actions     |
| `RequireOperatorOrAdmin`  | Operator, Admin          | Campaign launch actions              |
| `RequireAuthenticated`    | Any authenticated user   | Dashboard, read-only views           |

**Fallback policy:** All pages and API endpoints require authentication by default.
Unauthenticated requests are redirected to `/Account/Login`.

---

## Authentication Flow

1. User navigates to any protected page/endpoint.
2. If no authentication cookie is present, the request is redirected to `/Account/Login`.
3. User submits credentials. `SignInManager.PasswordSignInAsync` validates against ASP.NET Core Identity.
4. On success: authentication cookie set; `LastLoginAt` updated; audit event `Login` recorded.
5. On failure: audit event `LoginFailed` recorded. After 5 failures, account is locked for 15 minutes.
6. Logout: POST to `/Account/Logout`; cookie cleared; audit event `Logout` recorded.

---

## Audit Trail

All security events are persisted to the `AuthAuditLogs` table via `IAuthAuditService`.

| Event Type        | Triggered By                                      |
|-------------------|---------------------------------------------------|
| `Login`           | Successful sign-in                                |
| `LoginFailed`     | Invalid credentials or account lockout            |
| `Logout`          | User sign-out                                     |
| `UserCreated`     | Admin creates a new user                          |
| `UserDeactivated` | Admin deactivates a user account                  |
| `UserReactivated` | Admin reactivates a user account                  |
| `RoleAssigned`    | Admin assigns a role to a user                    |
| `RoleRemoved`     | Admin removes a role from a user                  |
| `PasswordChanged` | User changes their password (future)              |

---

## Password Policy

| Setting                   | Value        |
|---------------------------|-------------|
| Minimum length            | 8 characters |
| Require uppercase         | Yes          |
| Require lowercase         | Yes          |
| Require digit             | Yes          |
| Require non-alphanumeric  | No           |
| Lockout after failures    | 5 attempts   |
| Lockout duration          | 15 minutes   |

---

## Implementation Files

| File                                                                    | Purpose                                   |
|-------------------------------------------------------------------------|-------------------------------------------|
| `Domain/Enums/UserRole.cs`                                              | Role name constants + All/Default helpers |
| `Domain/Entities/ApplicationUser.cs`                                    | IApplicationUser domain interface         |
| `Domain/Entities/AuthAuditLog.cs`                                       | Audit log entity + AuthEventType constants|
| `Infrastructure/Identity/ApplicationUser.cs`                            | EF Identity user entity                   |
| `Infrastructure/Identity/ApplicationRole.cs`                            | EF Identity role entity                   |
| `Infrastructure/Identity/AuthAuditService.cs`                           | Audit log persistence                     |
| `Infrastructure/Identity/CurrentUserService.cs`                         | HTTP context user resolver                |
| `Application/DependencyInjection/AuthorizationPolicies.cs`              | Policy name constants                     |
| `Application/DependencyInjection/ServiceCollectionExtensions.cs`        | Policy registration via AddAuthorizationCore |
| `Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`     | Identity + cookie auth registration       |
| `Web/Pages/Account/Login.cshtml(.cs)`                                   | Login page with audit logging             |
| `Web/Pages/Account/Logout.cshtml(.cs)`                                  | Logout page with audit logging            |
| `Web/Pages/Account/AccessDenied.cshtml(.cs)`                            | 403 forbidden page                        |
| `Web/Pages/Admin/Users/Index.cshtml(.cs)`                               | User list (Admin only)                    |
| `Web/Pages/Admin/Users/Create.cshtml(.cs)`                              | Create user form (Admin only)             |
| `Web/Pages/Admin/Users/EditRole.cshtml(.cs)`                            | Role assignment form (Admin only)         |
| `Web/Controllers/UsersController.cs`                                    | REST API for user management (Admin only) |
