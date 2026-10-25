using System.Globalization;
using System.Text.Json;
using HelpDesk.Application.Tickets;
using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Enums;
using Microsoft.AspNetCore.Http.Features;

namespace HelpDesk.Api.Endpoints;

public static class TicketUploadEndpoints
{
    private static readonly FormOptions UploadOptions = new()
    {
        MultipartBodyLengthLimit = AttachmentLimits.MaxFileBytes,
        ValueLengthLimit = 16 * 1024,
        ValueCountLimit = 16
    };

    public static async Task<IResult> CreateAsync(HttpRequest request, ITicketService service, CancellationToken ct) =>
        await ReadUploadAsync(request, async () =>
        {
            TicketResult<TicketDetail> result;
            if (request.HasJsonContentType())
            {
                var body = await request.ReadFromJsonAsync<CreateTicketRequest>(ct);
                if (body is null)
                    return BadInput();
                result = await service.CreateAsync(body, ct);
            }
            else
            {
                if (!request.HasFormContentType)
                    return Results.StatusCode(415);
                var form = await request.ReadFormAsync(UploadOptions, ct);
                if (!int.TryParse(form["category"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var category))
                    return BadInput();
                result = await service.CreateAsync(new(form["title"].ToString(), form["description"].ToString(), (TicketCategory)category), Files(form), ct);
            }
            return result.Status == TicketResultStatus.Success ? Results.Created($"/tickets/{result.Value!.Ticket.Id}", result.Value) : TicketEndpoints.ToHttp(result);
        });

    public static void MapTicketUploadEndpoints(this IEndpointRouteBuilder routes)
    {
        var tickets = routes.MapGroup("/tickets/{ticketId:long}").RequireAuthorization();
        tickets.MapGet("/attachments", async (long ticketId, ITicketAttachmentService service, CancellationToken ct) => TicketEndpoints.ToHttp(await service.ListAsync(ticketId, ct)));
        tickets.MapGet("/attachments/{attachmentId:long}/download", async (long ticketId, long attachmentId, HttpContext context,
            ITicketAttachmentService service, CancellationToken ct) =>
        {
            var result = await service.DownloadAsync(ticketId, attachmentId, ct);
            if (result.Status != TicketResultStatus.Success)
                return TicketEndpoints.ToHttp(result);
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
            return Results.File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
        });
    }

    private static AttachmentUpload[] Files(IFormCollection form) => form.Files
        .Select(file => new AttachmentUpload(file.FileName, file.Length, file.OpenReadStream)).ToArray();

    private static async Task<IResult> ReadUploadAsync(HttpRequest request, Func<Task<IResult>> action)
    {
        if (request.ContentLength > AttachmentLimits.MaxRequestBytes)
            return Results.StatusCode(413);
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = AttachmentLimits.MaxRequestBytes;
        try
        {
            return await action();
        }
        catch (JsonException) { return BadInput(); }
        catch (InvalidDataException) { return Results.Problem(statusCode: 400, title: "Invalid upload", detail: "Upload at most 5 files, each no larger than 10 MB. Check the multipart request and file sizes."); }
        catch (BadHttpRequestException exception) when (exception.StatusCode == 413) { return Results.StatusCode(413); }
    }

    private static IResult BadInput() => Results.Problem(statusCode: 400, title: "Invalid Ticket request", detail: "Check the required fields and numeric category.");
}
