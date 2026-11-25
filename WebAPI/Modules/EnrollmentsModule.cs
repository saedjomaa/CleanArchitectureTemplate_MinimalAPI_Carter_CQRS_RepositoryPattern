using Application.Common.Abstractions;
using Application.Common.Pagination;
using Application.Common.Responses;
using Application.Common.Results;
using Application.Enrollments.Commands.CancelEnrollment;
using Application.Enrollments.Commands.EnrollStudent;
using Application.Enrollments.Queries.GetEnrollment;
using Application.Enrollments.Queries.ListStudentCourses;
using Carter;
using WebAPI.Diagnostics;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace WebAPI.Modules;

public sealed class EnrollmentsModule : ICarterModule
{
    private const string EnrollmentRoute = "/enrollments/{courseId:guid}";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var student = app.MapGroup("api/students/{studentId:guid}").WithTags("Enrollments");

        student.MapPost("/enrollments", Create)
            .WithSummary("Create a course enrollment for a student")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        student.MapGet(EnrollmentRoute, Get)
            .WithSummary("Get a student's enrollment in a course")
            .Produces<EnrollmentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        student.MapDelete(EnrollmentRoute, Cancel)
            .WithSummary("Cancel a student's enrollment and release the seat")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        student.MapGet("/courses", ListEnrolledCourses)
            .WithSummary("List courses a student is enrolled in")
            .Produces<PagedResult<StudentCourseResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Create(
        Guid studentId,
        EnrollStudentRequest request,
        ICommandHandler<EnrollStudentCommand, Result> handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new EnrollStudentCommand(studentId, request.CourseId), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblemDetails(context);
    }

    private static async Task<IResult> Get(
        Guid studentId,
        Guid courseId,
        IQueryHandler<GetEnrollmentQuery, Result<EnrollmentResponse>> handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetEnrollmentQuery(studentId, courseId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails(context);
    }

    private static async Task<IResult> Cancel(
        Guid studentId,
        Guid courseId,
        ICommandHandler<CancelEnrollmentCommand, Result> handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CancelEnrollmentCommand(studentId, courseId), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblemDetails(context);
    }

    private static async Task<IResult> ListEnrolledCourses(
        Guid studentId,
        IQueryHandler<ListStudentCoursesQuery, Result<PagedResult<StudentCourseResponse>>> handler,
        HttpContext context,
        CancellationToken cancellationToken,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize)
    {
        var result = await handler.HandleAsync(new ListStudentCoursesQuery(studentId, page, pageSize), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails(context);
    }
}

public sealed record EnrollStudentRequest(Guid CourseId);
