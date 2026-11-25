# AGENTS.md

Business rules, the endpoint list and project-specific technical facts (database provider, the value of `[Project]`, known exceptions) live in `Requirements.md`. Read it before adding or changing a use case. This file describes how the code is written and is the same for every project that uses this architecture.

Placeholders used below: `[Project]` (project name prefix, e.g. for the `DbContext`), `[Module]` (feature folder, plural), `[Entity]` (domain entity), `[UseCaseName]`, `[Action]`, `[Property]`, `[Reason]`.

## Stack
- ASP.NET Core **Minimal API** organized in **Carter** modules. **No MVC controllers** (no `AddControllers`, `ControllerBase` or MVC filters). The target framework, nullable and implicit usings are set once in `Directory.Build.props`.
- Clean Architecture + CQRS + Repository Pattern with Unit of Work
- **No MediatR/Mediator. No AutoMapper** (direct handler injection, manual mapping only)
- Carter, FluentValidation, EF Core, Scrutor, Serilog

## Layers (dependencies point inward only)
- `Domain`: entities, enums, domain exceptions. References nothing.
- `Application`: commands, queries, handlers, validators, responses, results, repository interfaces and `IUnitOfWork`. References Domain only. **Application has no EF Core package reference**; keep it that way.
- `Infrastructure`: EF Core, `DbContext`, repository and Unit of Work implementations, migrations, external services. Implements Application interfaces.
- `WebAPI`: Carter modules (`WebAPI/Modules`), endpoint filters and diagnostics (`WebAPI/Diagnostics`), `Program.cs`. Composition root. Only this layer differs from the Controllers version of this architecture; Domain, Application and Infrastructure are identical.

Handlers depend on repository interfaces and `IUnitOfWork`, never on `DbContext` or other Infrastructure types.

## Data Access (Repositories and Unit of Work)
- Repository interfaces and `IUnitOfWork` live in `Application/Common/Abstractions`. Implementations live in `Infrastructure/Persistence/Repositories` (repositories) and `Infrastructure/Persistence` (`[Project]UnitOfWork`).
- One repository per aggregate or feature area: `I[Entity]Repository`.
- `DbContext`, `DbSet<T>`, `IQueryable<T>` and provider-specific types never leave Infrastructure. Repository methods return domain entities, response records from `Application/Common/Responses`, `PagedResult<T>`, `bool`, or small records that group what a command needs (e.g. `[Entity]For[UseCaseName]`).
- Method kinds:
  - **Writes**: `Add` / `Remove` only stage changes. They never call `SaveChangesAsync`.
  - **Loads for a command**: return tracked domain entities, named after the use case that needs them (`GetFor[UseCaseName]Async`).
  - **Reads for a query**: return response records directly (`GetByIdAsync`, `ListAsync`).
  - **Checks**: `ExistsAsync` returns `bool`.
- Before adding a new repository method, check whether an existing one already fits. Do not add a generic repository (`IRepository<T>`).
- `IUnitOfWork.SaveChangesAsync` commits once per command and returns `Result`. It translates persistence failures in one place:
  - `DbUpdateConcurrencyException` → `PersistenceErrors.ConcurrencyConflict`
  - Provider-specific primary-key or unique violation → `PersistenceErrors.DuplicateKey`
- Handlers translate a `PersistenceErrors` value to the module's own error when the meaning is known (e.g. `DuplicateKey` → `[Module]Errors.[Reason]`). Otherwise they return the result as is. Handlers never use `try/catch` for persistence errors.
- Repositories are registered automatically as scoped services in `AddInfrastructure()` with Scrutor: every class in the Infrastructure assembly whose name ends with `Repository` is registered under its matching interface (`AsMatchingInterface`: `[Entity]Repository` → `I[Entity]Repository`). No manual registration is needed, but the class and interface names must match exactly. A class ending with `Repository` without a matching interface is not registered. Keep repositories in `Infrastructure/Persistence/Repositories` for organization; the scan does not depend on the folder.
- `IUnitOfWork` is registered manually (`[Project]UnitOfWork` does not follow the `I[ClassName]` naming). Everything is scoped, so repositories and the Unit of Work share the same `DbContext` per request.

## EF Core Entity Configuration
- Each persisted entity must define its EF Core mapping in `[Entity]Configuration.cs` under `Infrastructure/Persistence/Configurations` using `IEntityTypeConfiguration<[Entity]>`.
- `OnModelCreating` must apply these configurations and, when the project has seed data, call `[Project]Seed.Configure(modelBuilder)`.
- Column lengths come from the entity's constants (e.g. `[Entity].Maximum[Property]Length`), never from literal numbers.
- Enforce uniqueness in the database (keys or unique indexes), not only in handlers.

## Migrations
- Create migrations with the EF Core CLI only, from the repository root:
  `dotnet ef migrations add <Name> --project Infrastructure --startup-project WebAPI --output-dir Persistence/Migrations`
- Do not hand-write new migrations and do not edit a migration that has already been applied. Change the model, then add a new migration, and check that it preserves existing data.
- After a persistence refactor, check for pending model changes (`dotnet ef migrations has-pending-model-changes --project Infrastructure --startup-project WebAPI`).
- Seed data lives in `[Project]Seed`. Changing it requires a new migration.
- Known exceptions to these rules (e.g. an old hand-written migration) are listed in `Requirements.md`. Do not copy their style.
- The database is migrated automatically at startup (`MigrateDatabaseAsync` in `Program.cs`).

## Application Structure
```
Application/
  Common/
    Abstractions/   (ICommandHandler, IQueryHandler, I[Entity]Repository, IUnitOfWork)
    Decorators/     (ValidationDecorator, LoggingDecorator)
    Pagination/     (Pagination, PageQueryValidator)
    Responses/      (every response record: [Entity]Response, ...)
    Results/        (Result, Error, PagedResult)
  [Module]/
    Commands/[UseCaseName]/
    Queries/[UseCaseName]/
  Shared/
    Errors/[Module]Errors.cs, PersistenceErrors.cs
  DependencyInjection.cs
```

## File Naming
- `[UseCaseName]Command.cs` / `[UseCaseName]Query.cs`
- `[UseCaseName]CommandHandler.cs` / `[UseCaseName]QueryHandler.cs`
- `[UseCaseName]CommandValidator.cs` / `[UseCaseName]QueryValidator.cs`
- `[ResponseName]Response.cs`: every response record lives in `Application/Common/Responses/` with the namespace `Application.Common.Responses`, one file per response, whether one or many use cases return it.
- `[UseCaseName]Mapping.cs` only if the use case has 2+ mappings; otherwise map inside the handler.
- `[Entity]Repository.cs` implementing `I[Entity]Repository`.

## Rules
- Commands change state and return `Result<T>` when there is a response payload (e.g. `Result<Guid>` for a created entity ID), or `Result` when there is no payload. Queries are read-only and return `Result<TResponse>`.
- Commands and queries are plain records. No marker interfaces.
- Handlers: `internal sealed`, primary constructor, return `Result` or `Result<T>` according to the command or query contract, always pass `CancellationToken`.
- For expected failures (NotFound, Validation, Conflict), return a failed `Result` or `Result<T>` containing an `Error`.
- Use `ValidationDecorator` to validate commands and queries that have a registered validator. Use a logging decorator for handler execution. Follow the Transactions section for transaction handling.
- DI: `Application` exposes `AddApplication()`, and `Infrastructure` exposes `AddInfrastructure()`. In `AddApplication()`, use Scrutor to register command and query handlers and validators as scoped services. Set `publicOnly: false` in `AddClasses` to include `internal sealed` types, and exclude open generic decorators from the scan.

## Mapping
- Commands: build entities through domain factory methods (`[Entity].Create(...)`), not object initializers. Use a `ToEntity` extension only when the factory call needs non-trivial argument mapping.
- Queries: EF Core projections live in repositories. Use `AsNoTracking()` and project directly to the response with `Select`. Never load the entity and map it in memory.
- When a repository uses the same projection in 2+ methods, define it once as a `private static readonly Expression<Func<[Entity], [Entity]Response>> ToResponse` in that repository and pass it to `Select`.

## Validation
- Every command has a validator.
- Every query that takes an ID or is paginated has a validator. IDs are checked with `NotEmpty()` (route constraint `{id:guid}` still accepts `Guid.Empty`).
- Paginated query validators inherit from `PageQueryValidator<T>`.
- Validators mirror domain constraints using the entity's constants (`[Entity].Maximum[Property]Length`), so invalid user input is rejected before reaching the domain.
- Domain factory methods still guard their invariants with `ArgumentException` / `ArgumentOutOfRangeException`. These only fire on programming errors and end up in the global exception handler as a 500.

## Errors
- Each module has an `internal static class [Module]Errors` in `Application/Shared/Errors`, with `public static readonly Error` fields. Persistence-level errors live in `PersistenceErrors`.
- Error codes use the form `[Module].[Reason]` with a plural module name, e.g. `[Module].NotFound`.
- `ErrorType` maps to HTTP status in `ResultExtensions.ToProblemDetails`: `Validation` → 400, `NotFound` → 404, `Conflict` → 409, `BadGateway` → 502, `GatewayTimeout` → 504. The error code is returned in the `code` extension of the `ProblemDetails`.

## Endpoints (Minimal API + Carter)
- One Carter module per module: `public sealed class [Module]Module : ICarterModule` in `WebAPI/Modules/[Module]Module.cs`. `AddRoutes` creates one route group (`app.MapGroup("api/[module]").WithTags("[Module]")`) and maps every endpoint of the module on it.
- Modules are discovered by `AddCarter(new DependencyContextAssemblyCatalog(typeof(Program).Assembly))` (WebAPI only) and mapped by `MapCarter()` inside one root group in `Program.cs` that carries `ProblemDetailsLoggingFilter`. No manual registration.
- Services that `AddControllers()` used to register implicitly must be registered explicitly. In particular `AddAuthorization()` is required while `UseAuthorization()` is in the pipeline; without it the app fails at startup.
- Each endpoint points to a `private static async Task<IResult>` method of the module (not an inline lambda), named after the use case (`Create`, `Get`, `List`, ...). Inside the module file, alias `using IResult = Microsoft.AspNetCore.Http.IResult;` because `Application.Common.Results` also defines `IResult`.
- No business logic. The method's parameters are bound by Minimal API: route values and query values by name, the body record, the handler (`ICommandHandler<,>` / `IQueryHandler<,>`, resolved from DI without `[FromServices]`), `HttpContext` and `CancellationToken`. Call the handler once and convert a failed `Result` with `result.ToProblemDetails(context)`.
- Request body:
  - When the body maps 1:1 to the command, bind the command record directly (e.g. `Create[Entity]Command`).
  - When the command also needs route values, define a `[Action]Request` record at the bottom of the module file and build the command in the method.
- Responses (`TypedResults`):
  - Create command returning an ID → `TypedResults.CreatedAtRoute(id, "Get[Entity]", new { id })`. The matching GET endpoint is named with `.WithName("Get[Entity]")`; route names are unique across the API.
  - Command without payload → `TypedResults.NoContent()`.
  - Query → `TypedResults.Ok(value)`.
- Every endpoint declares its OpenAPI metadata: `.WithSummary(...)`, `.Produces<T>(status)` for the success response, `.ProducesValidationProblem()` for 400, and `.ProducesProblem(status)` for every other error status it can return (404, 409, 502, 504).
- List endpoints take `int page = Pagination.DefaultPage` and `int pageSize = Pagination.DefaultPageSize` as method parameters (bound from the query string).
- Keep `WebAPI/WebAPI.http` in sync with the endpoints: add an example for every new endpoint and remove examples for deleted ones.
- API documentation, Development only: `MapOpenApi()` serves the OpenAPI document at `/openapi/v1.json`, and `MapScalarApiReference()` (package `Scalar.AspNetCore`) serves an interactive UI at `/scalar` to read and call the endpoints. Both are generated from the endpoint metadata above. Do not expose them outside Development.

## External Services
Placeholder: `[Service]` (external API name, e.g. the provider's product name).
- Application owns the contract: `I[Service]Client` in `Application/Common/Abstractions`, returning `Result<T>` with response records from `Application/Common/Responses`. Application never references `HttpClient`, HTTP status codes or the external JSON shapes.
- Each external service is one Application module named after the service, not one module per external resource:
  - use cases in `Application/[Service]/Commands/[UseCaseName]/` and `Application/[Service]/Queries/[UseCaseName]/`, named by the resource (e.g. `ListUsers`, `GetUser`, `CreatePost`);
  - responses `[Service][Resource]Response` in `Application/Common/Responses/` (e.g. `[Service]UserResponse`);
  - the service's own errors in `Application/Shared/Errors/[Service]Errors.cs`, with codes `[Service].[Reason]` (e.g. `[Service].UserNotFound`).
- Infrastructure owns the implementation in `Infrastructure/ExternalServices/[Service]/`:
  - `ExternalServicesOptions` (`Infrastructure/ExternalServices/`): settings shared by all external services, bound from the `ExternalServices` section. It holds one `TimeoutSeconds` used by every client; it is set in configuration and not validated in code. Do not add a per-service timeout.
  - `[Service]Options`: the service's own settings (its `BaseUrl`), bound from `ExternalServices:[Service]` with `BindConfiguration`. `BaseUrl` is written in configuration as an absolute URL ending with `/`; it is not validated or normalized in code. Never hard-code URLs or timeouts.
  - `[Service]Models.cs`: `internal` records matching the external JSON. They never leave Infrastructure; the client maps them manually to response records.
  - `[Service]Client`: `internal sealed` typed HttpClient registered with `AddHttpClient<I[Service]Client, [Service]Client>` in `AddInfrastructure()`, which sets `BaseAddress = new Uri([Service]Options.BaseUrl)` and `Timeout` from `ExternalServicesOptions.TimeoutSeconds`.
- The client translates every expected failure in one private `SendAsync` helper and never throws for them:
  - HTTP 404 → `ExternalServiceErrors.NotFound`
  - any other non-success status or `HttpRequestException` → `ExternalServiceErrors.Unavailable` (502)
  - `OperationCanceledException` while the caller's token is not cancelled (the timeout elapsed) → `ExternalServiceErrors.Timeout` (504)
  - `JsonException`, an empty body, or a missing expected header → `ExternalServiceErrors.InvalidResponse` (502)
  - a cancellation requested by the caller is rethrown.
  Each failure is logged as a Warning with method and URL.
- Handlers translate `ExternalServiceErrors.NotFound` to the service's own error (e.g. `[Service]Errors.UserNotFound`) and return other errors as they are.
- External list endpoints still return `PagedResult<T>`: use the external API's paging parameters and total-count header when it has them; do not fetch everything and page in memory.
- No automatic retries by default. Never retry non-idempotent requests (POST) automatically.
- External services are not added to `/health`: their outage does not make this application unhealthy.
- One Carter module per external service: `[Service]Module` with the route group `api/[service]` (lowercase service name), holding all of that service's endpoints (e.g. `api/[service]/[resource]`). Do not split a service's endpoints across modules per resource.
- A create call to an external API with no local read endpoint returns `201` via `TypedResults.Created((string?)null, value)`.

## Cross-Cutting Rules

**Global error handling**
- Unexpected exceptions are handled by a single `IExceptionHandler` registered in WebAPI that returns a uniform `ProblemDetails`.
- Never return stack traces or exception messages to the client. Log the exception server-side.
- Every failed response is logged with the exact `ProblemDetails` JSON sent to the client (`Responded {StatusCode} with {ProblemDetails}`): `ProblemDetailsLoggingFilter`, an `IEndpointFilter` on the root group of all Carter modules, logs it for every `ProblemHttpResult` an endpoint returns (Warning for 4xx, Error for 5xx), and `GlobalExceptionHandler` logs it for exceptions. Successful responses are not logged this way.
- Every ProblemDetails is built by `ProblemDetailsBuilder` (`WebAPI/Diagnostics`), which fills `type`, `title`, `status`, `detail`, `traceId` and `correlationId` before the response is written (Minimal API has no MVC `ProblemDetailsFactory`). `ToProblemDetails` returns `TypedResults.Problem(problem)`.
- Request binding failures (invalid JSON, missing body, a value of the wrong type): `RouteHandlerOptions.ThrowOnBadRequest` is enabled, so Minimal API throws `BadHttpRequestException` in every environment and `GlobalExceptionHandler` returns 400 with code `Request.Invalid` (no field-level `errors`). Field-level validation stays in the validators and returns `Validation.Failed`.

**Transactions**
- One `IUnitOfWork.SaveChangesAsync` per command is the default and is already atomic.
- If a command needs multiple saves that must succeed together, expose an explicit transaction through an Application interface. No global transaction decorator.

**Concurrency**
- Use a concurrency token (`RowVersion`, a `Guid` regenerated by the entity on every change) on entities that support concurrent edits.
- Include `rowVersion` in read responses and require it in update requests.
- Use the client-supplied `rowVersion` as the expected version when saving the update.
- Return a `Conflict` error (HTTP 409) if the version is stale or the Unit of Work returns `PersistenceErrors.ConcurrencyConflict`.
- An operation that changes a counter or state on another entity as a side effect does not take a client `rowVersion`, but still maps `PersistenceErrors.ConcurrencyConflict` to a module `Conflict` error.

**Pagination and filtering**
- Every list query returns `Result<PagedResult<T>>` (items, page, pageSize, totalCount). Repositories build it with `ToPageAsync` from `Infrastructure/Persistence/PagedQueryExtensions.cs`.
- Page size has a default (`Pagination.DefaultPageSize`) and a hard maximum (`Pagination.MaximumPageSize`) enforced in the query validator.
- Every list query has a deterministic order: a business field first, then `Id` as a tie-breaker.
- Client-side filtering and sorting use one shared pattern (contract in `Application/Common/Pagination`, applied in Infrastructure). If it does not exist yet, design it, document it here, and only then use it. Do not add ad-hoc filter parameters to a single endpoint.

**Domain entities**
- Entities are not anemic: business rules live inside them (`[Entity].Create(...)`, `entity.Cancel()`), with private setters and a private parameterless constructor for EF Core.
- Handlers orchestrate (load through repositories, call domain methods, save through `IUnitOfWork`). They do not contain business rules.
- Checks that need the database (an entity exists, a duplicate exists) are done in the handler through repository methods before calling the domain method, and return the matching `Error`.

**Observability**
- Use **Serilog** for structured logging (inject `ILogger<T>`, no static `Log` calls in application code) and Serilog request logging for HTTP.
- Serilog sinks and minimum levels are configured only in the `Serilog` section of `appsettings*.json` (read with `ReadFrom.Configuration`), never in code. Logs go to the console and to daily rolling, human-readable text files `logs/log-yyyyMMdd.txt` under the app's working directory, kept for 14 days. The `logs/` folder is git-ignored. The file uses a `Serilog.Expressions` template that draws a separator block before every event whose `EventId.Name` is `RequestStarted`; `CorrelationIdMiddleware` logs that event (method, path, query string, correlation id) at the start of each request. Do not log other events with that EventId. To trace a request, search the files for its `CorrelationId` and read down from its separator. Lines from concurrent requests can interleave.
- EF Core sensitive data logging (SQL parameter values in logs and in EF exception messages) is switched by `Database:EnableSensitiveDataLogging` in `appsettings*.json` and passed to `AddInfrastructure()`. Never hard-code it. Where it is enabled, treat log files as containing personal data. The value each project uses is recorded in `Requirements.md`.
- Every request has a correlation id set by `CorrelationIdMiddleware` from `HttpContext.TraceIdentifier`. It is server-generated (an incoming header is not trusted), returned in the `X-Correlation-Id` response header, added to the log scope, and included as `correlationId` in every `ProblemDetails`.
- Expose `/health` with a database readiness check from the start.

## Testing
- If the solution has a test project, add or update tests for every changed use case and run them.
- Always verify changes by building the solution file (`*.slnx` / `*.sln`) at the repository root with `dotnet build` and by running the affected requests in `WebAPI/WebAPI.http`, including the failure cases.

## New Use Case Checklist
1. Check `Requirements.md` for the business rules; add the new rule there if it is missing.
2. Create the folder under `[Module]/Commands` or `[Module]/Queries`.
3. Add the command or query record, its handler, and its validator (see Validation). Add a response record where needed.
4. Add or reuse repository methods (see Data Access). A new repository is registered automatically when it is named `[Entity]Repository` and implements `I[Entity]Repository`.
5. Add errors to `[Module]Errors.cs` if needed.
6. If the model changed, add a migration with the EF Core CLI (see Migrations).
7. Add the endpoint to the module following the Endpoints section, and an example request in `WebAPI.http`.
8. Add the endpoint to `Requirements.md`.
9. Run `dotnet build` on the solution file at the repository root.
