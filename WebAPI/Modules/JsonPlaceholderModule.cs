using Application.Common.Abstractions;
using Application.Common.Pagination;
using Application.Common.Responses;
using Application.Common.Results;
using Application.JsonPlaceholder.Commands.CreatePost;
using Application.JsonPlaceholder.Queries.GetUser;
using Application.JsonPlaceholder.Queries.ListUsers;
using Carter;
using WebAPI.Diagnostics;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace WebAPI.Modules;

/// <summary>
/// Endpoints backed by the external JSONPlaceholder API (https://jsonplaceholder.typicode.com).
/// One module per external service; the database is not used here.
/// </summary>
public sealed class JsonPlaceholderModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var jsonPlaceholder = app.MapGroup("api/jsonplaceholder").WithTags("JsonPlaceholder");

        jsonPlaceholder.MapGet("/users", ListUsers)
            .WithSummary("List users from JSONPlaceholder")
            .Produces<PagedResult<JsonPlaceholderUserResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        jsonPlaceholder.MapGet("/users/{id:int}", GetUser)
            .WithSummary("Get a user from JSONPlaceholder")
            .Produces<JsonPlaceholderUserResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        jsonPlaceholder.MapPost("/posts", CreatePost)
            .WithSummary("Create a post in JSONPlaceholder (fake: JSONPlaceholder does not persist it)")
            .Produces<JsonPlaceholderPostResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);
    }

    private static async Task<IResult> ListUsers(
        IQueryHandler<ListUsersQuery, Result<PagedResult<JsonPlaceholderUserResponse>>> handler,
        HttpContext context,
        CancellationToken cancellationToken,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize)
    {
        var result = await handler.HandleAsync(new ListUsersQuery(page, pageSize), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails(context);
    }

    private static async Task<IResult> GetUser(
        int id,
        IQueryHandler<GetUserQuery, Result<JsonPlaceholderUserResponse>> handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetUserQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails(context);
    }

    private static async Task<IResult> CreatePost(
        CreatePostCommand command,
        ICommandHandler<CreatePostCommand, Result<JsonPlaceholderPostResponse>> handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);

        // There is no endpoint to read a single post, so the 201 has no Location header.
        return result.IsSuccess
            ? TypedResults.Created((string?)null, result.Value)
            : result.ToProblemDetails(context);
    }
}
