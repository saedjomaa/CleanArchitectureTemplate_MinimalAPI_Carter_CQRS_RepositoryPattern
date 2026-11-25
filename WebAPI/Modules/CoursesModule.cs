using Application.Common.Abstractions;
using Application.Common.Pagination;
using Application.Common.Responses;
using Application.Common.Results;
using Application.Courses.Commands.CreateCourse;
using Application.Courses.Queries.GetCourse;
using Application.Courses.Queries.ListCourses;
using Carter;
using WebAPI.Diagnostics;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace WebAPI.Modules;

public sealed class CoursesModule : ICarterModule
{
    private const string GetCourseRoute = "GetCourse";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var courses = app.MapGroup("api/courses").WithTags("Courses");

        courses.MapPost("/", Create)
            .WithSummary("Create a course")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        courses.MapGet("/{id:guid}", Get)
            .WithName(GetCourseRoute)
            .WithSummary("Get a course by ID")
            .Produces<CourseResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        courses.MapGet("/", List)
            .WithSummary("List courses")
            .Produces<PagedResult<CourseResponse>>()
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Create(
        CreateCourseCommand command,
        ICommandHandler<CreateCourseCommand, Result<Guid>> handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.CreatedAtRoute(result.Value, GetCourseRoute, new { id = result.Value })
            : result.ToProblemDetails(context);
    }

    private static async Task<IResult> Get(
        Guid id,
        IQueryHandler<GetCourseQuery, Result<CourseResponse>> handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetCourseQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails(context);
    }

    private static async Task<IResult> List(
        IQueryHandler<ListCoursesQuery, Result<PagedResult<CourseResponse>>> handler,
        HttpContext context,
        CancellationToken cancellationToken,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize)
    {
        var result = await handler.HandleAsync(new ListCoursesQuery(page, pageSize), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails(context);
    }
}
