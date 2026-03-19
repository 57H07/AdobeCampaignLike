# Guideline — Stack technique (projets .NET 8)

Ce document fournit un **gabarit générique, concis et réutilisable** pour une application en architecture en couches (`Domain` / `Application` / `Infrastructure` / `Web`).

## 1) Runtime & framework
- **.NET 8 (LTS)** : base applicative stable et maintenable.
- **ASP.NET Core Web** : UI serveur.
  - **Razor Pages** : recommandé pour une UI simple et structurée par page.
  - **MVC** : possible si le projet est orienté contrôleurs/vues.

## 2) Couches de solution
- **Domain** : entités, règles métier, enums, exceptions métier.
- **Application** : services applicatifs (cas d’usage), interfaces, DTO, mapping.
- **Infrastructure** : persistance, intégrations techniques, implémentations des interfaces.
- **Web** : point d’entrée HTTP, UI, middleware, DI.

## 3) Stack technique recommandée
- **ORM** : Entity Framework Core
- **Base de données** : SQL Server
- **Migration schéma** : EF Core Migrations
- **Mapping** : Mapster (`Entity <-> DTO`)
- **DI** : `Microsoft.Extensions.DependencyInjection`
- **Tests** : xUnit + Moq + FluentAssertions

## 4) Arborescence type (sans CQRS)
```text
/src
  /Solution.Domain
    /Common
    /Entities
    /Enums
    /Exceptions

  /Solution.Application
    /Interfaces
    /Services
    /DTOs
    /Mappings
    /Exceptions
    /Validators             (optionnel)

  /Solution.Infrastructure
    /Data
      ApplicationDbContext.cs
      /Configurations
      /Migrations
    /Repositories
    /DependencyInjection
    /Services               (optionnel)

  /Solution.Web
    Program.cs
    /Pages                  (si Razor Pages)
    /Controllers            (si MVC)
    /Views                  (si MVC)
    /ViewModels
    /Middleware
    /wwwroot

/tests
  /Solution.Application.Tests
  /Solution.Domain.Tests          (optionnel)
  /Solution.Infrastructure.Tests  (optionnel)
```

## 5) Règles de dépendances
- `Web -> Application`
- `Application -> Domain`
- `Infrastructure -> Application + Domain`
- `Domain` ne dépend d’aucune autre couche.

## 6) Principes d’implémentation
- Ne pas exposer les entités `Domain` directement à la couche `Web`.
- Passer par des `DTO` dans les échanges applicatifs.
- Garder les services applicatifs focalisés sur un cas d’usage.
- Centraliser l’enregistrement DI via `AddApplication()` et `AddInfrastructure()`.

---

## Résumé rapide
- **Backend**: .NET 8 + ASP.NET Core Web
- **Data**: EF Core + SQL Server + Migrations
- **Mapping**: Mapster
- **DI**: conteneur natif ASP.NET Core
- **Tests**: xUnit + Moq + FluentAssertions
