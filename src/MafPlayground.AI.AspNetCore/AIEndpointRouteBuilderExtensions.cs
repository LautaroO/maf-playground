using System.Text.Json;
using System.Buffers;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Budget;
using MafPlayground.AI.Runtime.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.AspNetCore;

public static class AIEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapAIEntities(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/api/ai")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        RouteHandlerBuilder route = endpoints.MapPost(
            $"{prefix.TrimEnd('/')}/entities/{{entityId}}/runs",
            RunEntityAsync);
        return route;
    }

    private static async Task<IResult> RunEntityAsync(
        string entityId,
        HttpContext httpContext,
        IAIEntityRegistry registry,
        IAIEntityInvoker invoker,
        IAuthorizationService authorization,
        IOptions<AIEndpointOptions> options,
        CancellationToken cancellationToken)
    {
        string executionId = Guid.NewGuid().ToString("N");
        try
        {
            AIEntityDescriptor? descriptor = registry.Find(entityId);
            if (descriptor is null)
            {
                return Problem(StatusCodes.Status404NotFound, "Unknown AI entity.", executionId);
            }

            AIEndpointOptions endpointOptions = options.Value;
            if (!descriptor.AllowAnonymous)
            {
                if (httpContext.User.Identity?.IsAuthenticated != true)
                {
                    return Problem(StatusCodes.Status401Unauthorized, "Authentication is required.", executionId);
                }

                AuthorizationResult result = await authorization.AuthorizeAsync(
                    httpContext.User,
                    resource: null,
                    descriptor.AuthorizationPolicy!).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return Problem(StatusCodes.Status403Forbidden, "Access is forbidden.", executionId);
                }
            }

            string? subject = httpContext.User.FindFirst(endpointOptions.SubjectClaimType)?.Value;
            if (string.IsNullOrWhiteSpace(subject) && descriptor.AllowAnonymous)
            {
                subject = "anonymous";
            }
            else if (string.IsNullOrWhiteSpace(subject))
            {
                return Problem(StatusCodes.Status401Unauthorized, "A caller subject claim is required.", executionId);
            }

            AIEntityRunRequest? request = await ReadRequestAsync(
                httpContext.Request,
                endpointOptions.MaxRequestBodyBytes,
                cancellationToken).ConfigureAwait(false);
            if (request is null || request.Input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return Problem(StatusCodes.Status400BadRequest, "A JSON input value is required.", executionId);
            }
            if (request.SessionId is { Length: > 200 } ||
                request.SessionId is not null && string.IsNullOrWhiteSpace(request.SessionId))
            {
                return Problem(StatusCodes.Status400BadRequest, "The session ID is invalid.", executionId);
            }

            AICallerContext caller = new(
                subject,
                httpContext.User.FindFirst(endpointOptions.TenantClaimType)?.Value);
            AIExecutionContext context = new(executionId, caller, request.SessionId);
            AIEntityInvocationResult invocation = await invoker.InvokeAsync(
                descriptor.Id,
                request.Input,
                context,
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(invocation);
        }
        catch (AIEntityInputException exception)
        {
            return Problem(StatusCodes.Status400BadRequest, exception.Message, executionId);
        }
        catch (AISessionNotSupportedException exception)
        {
            return Problem(StatusCodes.Status400BadRequest, exception.Message, executionId);
        }
        catch (AISessionOwnershipException)
        {
            return Problem(StatusCodes.Status403Forbidden, "Access to the session is forbidden.", executionId);
        }
        catch (AISessionConcurrencyException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "The session was updated concurrently.",
                executionId);
        }
        catch (ContentLengthExceededException)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "The AI input is too large.", executionId);
        }
        catch (RequestBodyTooLargeException)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "The request is too large.", executionId);
        }
        catch (BudgetExceededException exception)
        {
            return Problem(
                StatusCodes.Status429TooManyRequests,
                $"The execution budget for '{exception.Resource}' was exceeded.",
                executionId);
        }
        catch (JsonException)
        {
            return Problem(StatusCodes.Status400BadRequest, "The JSON request is invalid.", executionId);
        }
        catch (ArgumentException)
        {
            return Problem(StatusCodes.Status400BadRequest, "The AI entity input is invalid.", executionId);
        }
        catch (BadHttpRequestException)
        {
            return Problem(StatusCodes.Status400BadRequest, "The request is invalid.", executionId);
        }
        catch (TimeoutException)
        {
            return Problem(StatusCodes.Status504GatewayTimeout, "The AI provider timed out.", executionId);
        }
        catch (AIProviderFailureException)
        {
            return Problem(StatusCodes.Status502BadGateway, "The AI provider failed.", executionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Problem(
                StatusCodes.Status500InternalServerError,
                "The AI execution failed unexpectedly.",
                executionId);
        }
    }

    private static IResult Problem(int status, string detail, string executionId) =>
        Results.Problem(
            statusCode: status,
            title: ReasonPhrases.GetReasonPhrase(status),
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["executionId"] = executionId,
            });

    private static async ValueTask<AIEntityRunRequest?> ReadRequestAsync(
        HttpRequest request,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximumBytes)
        {
            throw new RequestBodyTooLargeException();
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using MemoryStream content = new();
            while (true)
            {
                int read = await request.Body.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                if (content.Length + read > maximumBytes)
                {
                    throw new RequestBodyTooLargeException();
                }

                await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }

            content.Position = 0;
            return await JsonSerializer.DeserializeAsync<AIEntityRunRequest>(
                content,
                AIJsonSerializerOptions.Default,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed record AIEntityRunRequest(JsonElement Input, string? SessionId);

    private sealed class RequestBodyTooLargeException : Exception;
}
