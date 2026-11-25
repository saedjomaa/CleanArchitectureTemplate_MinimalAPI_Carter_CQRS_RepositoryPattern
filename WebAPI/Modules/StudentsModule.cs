using Application.Common.Abstractions;
using Application.Common.Pagination;
using Application.Common.Responses;
using Application.Common.Results;
using Application.Students.Commands.CreateStudent;
using Application.Students.Queries.GetStudent;
using Application.Students.Queries.ListStudents;
using Carter;
using WebAPI.Diagnostics;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace WebAPI.Modules;

public sealed class StudentsModule : ICarterModule
{
    private const string GetStudentRoute = "GetStudent";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var students = app.MapGroup("api/students").WithTags("Students");

        students.MapPost("/", Create)
            .WithSummary("Create a student")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        students.MapGet("/{id:guid}", Get)
            .WithName(GetStudentRoute)
            .WithSummary("Get a student by ID")
            .Produces<StudentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        students.MapGet("/", List)
            .WithSummary("List students")
            .Produces<PagedResult<StudentResponse>>()
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Create(
        CreateStudentCommand command,
        ICommandHandler<CreateStudentCommand, Result<Guid>> handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.CreatedAtRoute(result.Value, GetStudentRoute, new { id = result.Value })
            : result.ToProblemDetails(context);
    }

    private static async Task<IResult> Get(
        Guid id,
        IQueryHandler<GetStudentQuery, Result<StudentResponse>> handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetStudentQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails(context);
    }

    private static async Task<IResult> List(
        IQueryHandler<ListStudentsQuery, Result<PagedResult<StudentResponse>>> handler,
        HttpContext context,
        CancellationToken cancellationToken,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize)
    {
        var result = await handler.HandleAsync(new ListStudentsQuery(page, pageSize), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails(context);
    }
}
